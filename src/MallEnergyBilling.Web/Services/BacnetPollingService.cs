using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Services;

public static class BacnetPointState
{
    public static void Apply(BacnetPoint p, BacnetPointResult r)
    {
        p.LastAttempt = r.Timestamp;
        p.Quality = r.Quality; p.LastError = r.Error;
        if (r.Quality == "Good" && r.Value.HasValue)
        { p.LastValue = r.Value; p.LastRawValue = r.Raw; p.LastSuccess = r.Timestamp; p.ConsecutiveFailures = 0; }
        else p.ConsecutiveFailures++;
    }
}

// Separate scheduler preserves the existing serial Modbus polling behavior. Each device
// has at most one active cycle; slow devices do not hold up scheduling of other devices.
public sealed class BacnetPollingService(IServiceScopeFactory scopes, IBacnetIpService bacnet, DatabaseMaintenanceService maintenance, ILogger<BacnetPollingService> log) : BackgroundService
{
    private readonly Dictionary<int, Task> running = [];
    private readonly Dictionary<int, DateTimeOffset> next = [];
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                foreach (var id in running.Where(x => x.Value.IsCompleted).Select(x => x.Key).ToArray()) { await running[id]; running.Remove(id); }
                try
                {
                    using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    List<Controller> controllers;
                    await maintenance.Gate.WaitAsync(ct);
                    try { controllers = await db.Controllers.AsNoTracking().Where(x => x.CommunicationType == "BacnetIp" && x.Enabled).ToListAsync(ct); }
                    finally { maintenance.Gate.Release(); }
                    foreach (var c in controllers.OrderBy(x => next.GetValueOrDefault(x.Id)))
                    {
                        if (running.Count >= 8) break;
                        if (running.ContainsKey(c.Id) || next.GetValueOrDefault(c.Id) > DateTimeOffset.UtcNow) continue;
                        next[c.Id] = DateTimeOffset.UtcNow.AddSeconds(c.PollingIntervalSeconds);
                        running[c.Id] = PollAsync(c.Id, ct);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { log.LogError(ex, "BACnet scheduler error"); }
                await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { await Task.WhenAll(running.Values); }
    }
    public async Task PollAsync(int id, CancellationToken ct)
    {
        try { await PollCore(id, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { log.LogError(ex, "BACnet device {Device} cycle failed", id); }
    }
    private async Task PollCore(int id, CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Controller? c; List<Meter> meters;
        await maintenance.Gate.WaitAsync(ct);
        try
        {
            c = await db.Controllers.SingleOrDefaultAsync(x => x.Id == id, ct);
            meters = await db.Meters.Include(x => x.BacnetPoints).Where(x => x.ControllerId == id && x.Active).ToListAsync(ct);
        }
        finally { maintenance.Gate.Release(); }
        if (c is null || !c.Enabled || c.CommunicationType != "BacnetIp" || meters.Count == 0) return;
        c.BacnetLastAttempt = DateTimeOffset.UtcNow;
        var previous = c.Condition; var previousError = c.BacnetLastError;
        var points = meters.SelectMany(x => x.BacnetPoints).Where(x => x.Enabled).ToArray();
        IReadOnlyList<BacnetPointResult> results;
        try
        {
            BacnetNetwork.Validate(c);
            await bacnet.TestAsync(c, ct);
            c.LastSuccess = DateTimeOffset.UtcNow;
            results = await bacnet.ReadPointsAsync(c, points, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var error = BacnetIpService.Error(ex);
            results = points.Select(p => new BacnetPointResult(p.Id, null, null, ex is InvalidOperationException ? "Configuration Error" : ex is TimeoutException ? "Timeout" : "BACnet Error", error, DateTimeOffset.UtcNow)).ToArray();
            if (previousError != error) log.LogWarning(ex, "BACnet device {Device}: {Error}", c.Name, error);
        }
        foreach (var p in points)
        {
            var result = results.FirstOrDefault(x => x.PointId == p.Id) ?? new(p.Id, null, null, "Bad", "No point result", DateTimeOffset.UtcNow);
            var quality = p.Quality; BacnetPointState.Apply(p, result);
            if (quality != p.Quality) log.LogDebug("BACnet point {Point} quality changed from {Old} to {New}", p.Name, quality, p.Quality);
        }
        var good = points.Count(x => x.Quality == "Good");
        c.Condition = points.Length == 0 ? "Configuration Error" : good == points.Length ? "Online" : good > 0 ? "Partial Data" : results.FirstOrDefault()?.Quality == "Timeout" ? "Timeout" : results.FirstOrDefault()?.Quality == "Configuration Error" ? "Configuration Error" : "BACnet Error";
        c.BacnetLastError = points.Length == 0 ? "Configure and enable BACnet points." : string.Join("; ", results.Where(x => x.Quality != "Good").Select(x => x.Error).Distinct().Take(3));
        c.BacnetConsecutiveFailures = good > 0 ? 0 : c.BacnetConsecutiveFailures + 1;
        if (previous != c.Condition || previousError != c.BacnetLastError)
            log.LogInformation("BACnet device {Device}: {Status}; {Error}", c.Name, c.Condition, c.BacnetLastError);
        foreach (var meter in meters)
        {
            var energy = meter.BacnetPoints.Where(x => x.Enabled && x.Measurement == MeasurementType.TotalImportEnergy && x.BillingConfirmed).ToArray();
            meter.CommunicationStatus = c.Condition;
            if (energy.Length != 1) { meter.CommunicationStatus = "Configuration Error"; continue; }
            var point = energy[0];
            if (point.Quality != "Good" || point.LastValue is null) { meter.CommunicationStatus = "Stale"; continue; }
            var value = point.LastValue.Value;
            if (value < 0 || meter.LastReadingAt is not null && value < meter.LastReading)
            {
                if (!meter.BacnetCounterReview)
                {
                    db.MeterReadings.Add(new() { MeterId = meter.Id, AccumulatedKwh = value, Timestamp = point.LastSuccess!.Value, Source = ReadingSource.Automatic, Quality = "BACnet counter decrease: review required", RequiresReview = true });
                    log.LogWarning("BACnet cumulative energy decreased for {Meter}; billing suspended for review", meter.Name);
                }
                meter.BacnetCounterReview = true;
            }
            if (meter.BacnetCounterReview) { meter.CommunicationStatus = "Counter Review"; continue; }
            meter.LastReading = value; meter.LastReadingAt = point.LastSuccess;
            var last = await db.MeterReadings.Where(x => x.MeterId == meter.Id).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (last is null || point.LastSuccess - last.Timestamp >= TimeSpan.FromMinutes(15))
                db.MeterReadings.Add(new() { MeterId = meter.Id, AccumulatedKwh = value, RawValue = point.LastRawValue is >= 0 and <= ulong.MaxValue ? (ulong)point.LastRawValue.Value : 0, Timestamp = point.LastSuccess!.Value, Source = ReadingSource.Automatic, Quality = "Good" });
        }
        await maintenance.Gate.WaitAsync(ct);
        try
        {
            // A commissioning edit during network I/O invalidates this cycle's results.
            using var verifyScope = scopes.CreateScope(); var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var current = await verifyDb.Controllers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (current is null || !current.Enabled || Signature(current) != Signature(c)) return;
            var currentPoints = await verifyDb.BacnetPoints.AsNoTracking().Where(x => x.Meter!.ControllerId == id && x.Meter.Active).ToListAsync(ct);
            if (!currentPoints.OrderBy(x => x.Id).Select(PointSignature).SequenceEqual(meters.SelectMany(x => x.BacnetPoints).OrderBy(x => x.Id).Select(PointSignature))) return;
            await db.SaveChangesAsync(ct);
        }
        finally { maintenance.Gate.Release(); }
    }
    private static string Signature(Controller c) => $"{c.CommunicationType}/{c.IpAddress}/{c.BacnetDeviceInstance}/{c.BacnetLocalIp}/{c.BacnetUdpPort}/{c.TimeoutMilliseconds}/{c.RetryCount}";
    private static string PointSignature(BacnetPoint p) => $"{p.Id}/{p.MeterId}/{p.ObjectType}/{p.ObjectInstance}/{p.PropertyId}/{p.Scale}/{p.Offset}/{p.Measurement}/{p.Enabled}/{p.BillingConfirmed}/{p.Unit}";
}
