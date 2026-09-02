using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Services;

public sealed class MeterPollingService(IServiceScopeFactory scopes, IModbusRtuService modbus, DatabaseMaintenanceService maintenance, ILogger<MeterPollingService> log) : BackgroundService
{
    readonly Dictionary<int, DateTimeOffset> nextPoll = [];
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                List<int> due;
                await maintenance.Gate.WaitAsync(ct);
                try
                {
                    using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var now = DateTimeOffset.UtcNow;
                    due = await db.Controllers.Where(x => x.Enabled && x.CommunicationType == "ModbusRtu" && db.Meters.Any(m => m.ControllerId == x.Id && m.Active)).Select(x => x.Id).ToListAsync(ct);
                    due = due.Where(id => !nextPoll.TryGetValue(id, out var at) || at <= now).ToList();
                }
                finally { maintenance.Gate.Release(); }
                foreach (var id in due) await PollController(id, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) { log.LogError(ex, "Polling cycle failed; retrying"); }
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    async Task PollController(int controllerId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Controller? controller; List<Meter> meters;
        await maintenance.Gate.WaitAsync(ct);
        try { controller = await db.Controllers.FirstOrDefaultAsync(x => x.Id == controllerId, ct); meters = await db.Meters.Where(x => x.ControllerId == controllerId && x.Active).OrderBy(x => x.StartingRegister).ToListAsync(ct); }
        finally { maintenance.Gate.Release(); }
        if (controller is null) return;
        nextPoll[controllerId] = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, controller.PollingIntervalSeconds));
        var transportFailed = false;
        foreach (var meter in meters)
        {
            if (transportFailed) { meter.CommunicationStatus = "Failed"; continue; }
            try
            {
                var count = ModbusValueConverter.RegisterCount(meter.DataType); var result = await modbus.ReadHoldingRegistersAsync(controller, meter.StartingRegister, count, ct); var value = ModbusValueConverter.ConvertValue(result.Registers, meter.DataType, meter.WordOrder, meter.ScalingFactor);
                if (value < meter.LastReading && meter.LastReadingAt is not null) db.MeterReadings.Add(new() { MeterId = meter.Id, RawValue = (ulong)Math.Max(0, value / meter.ScalingFactor), AccumulatedKwh = value, Timestamp = DateTimeOffset.UtcNow, Source = ReadingSource.Automatic, Quality = "Review: lower than previous", RequiresReview = true });
                meter.LastReading = value; meter.LastReadingAt = DateTimeOffset.UtcNow; meter.CommunicationStatus = "Connected"; controller.LastSuccess = DateTimeOffset.UtcNow; controller.Condition = "Connected"; controller.Notes = $"Last RTU request {result.RequestHex}; response {result.ResponseHex}";
                await SaveHistoryIfDue(db, meter, ct);
            }
            catch (Exception ex) { meter.CommunicationStatus = "Failed"; controller.Condition = "Failed"; controller.Notes = ex.Message; transportFailed = true; log.LogWarning(ex, "RTU read failed for controller {Controller} on {Port}; remaining channels skipped until next poll", controller.Name, controller.ComPort); }
        }
        await maintenance.Gate.WaitAsync(ct);
        try { await db.SaveChangesAsync(ct); }
        finally { maintenance.Gate.Release(); }
    }

    static async Task SaveHistoryIfDue(ApplicationDbContext db, Meter meter, CancellationToken ct)
    {
        if (meter.LastReadingAt is null || meter.CommunicationStatus != "Connected") return;
        var last = await db.MeterReadings.Where(x => x.MeterId == meter.Id).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (last is null || DateTimeOffset.UtcNow - last.Timestamp >= TimeSpan.FromMinutes(15)) db.MeterReadings.Add(new() { MeterId = meter.Id, RawValue = (ulong)Math.Max(0, meter.LastReading / meter.ScalingFactor), AccumulatedKwh = meter.LastReading, Timestamp = DateTimeOffset.UtcNow, Source = ReadingSource.Automatic, Quality = "Good" });
    }
}
