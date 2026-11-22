using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Services;

public sealed class MeterPollingService(IServiceScopeFactory scopes, IModbusService modbus, IBacnetIpService bacnet, IMBusService mbus, DatabaseMaintenanceService maintenance, ILogger<MeterPollingService> log) : BackgroundService
{
    readonly Dictionary<int, DateTimeOffset> nextPoll = [];
    /// <summary>Longest allowed polling interval (1 hour) plus a margin.</summary>
    public static readonly TimeSpan MaxPollWait = TimeSpan.FromSeconds(3600 + 60);
    /// <summary>Due when never polled, when the time has come, or when the next-poll time is further away than any interval (the PC clock was moved back).</summary>
    public static bool IsDue(DateTimeOffset? nextAt, DateTimeOffset now) => nextAt is null || nextAt <= now || nextAt > now + MaxPollWait;
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
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
                    due = await db.Controllers.Where(x => x.Enabled && (x.CommunicationType == "ModbusRtu" || x.CommunicationType == "ModbusTcp" || x.CommunicationType == "MBusTcp") && db.Meters.Any(m => m.ControllerId == x.Id && m.Active)).Select(x => x.Id).ToListAsync(ct);
                    // A next-poll time more than the longest interval away means the PC clock was moved back: poll now.
                    due = due.Where(id => IsDue(nextPoll.TryGetValue(id, out var at) ? at : null, now)).ToList();
                }
                finally { maintenance.Gate.Release(); }
                foreach (var id in due) await PollController(id, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) { log.LogError(ex, "Polling cycle failed; retrying"); }
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    async Task PollController(int controllerId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Controller? controller; List<Meter> meters; int generation;
        await maintenance.Gate.WaitAsync(ct);
        try { generation = maintenance.Generation; controller = await db.Controllers.FirstOrDefaultAsync(x => x.Id == controllerId, ct); meters = await db.Meters.Where(x => x.ControllerId == controllerId && x.Active).OrderBy(x => x.StartingRegister).ToListAsync(ct); }
        finally { maintenance.Gate.Release(); }
        if (controller is null) return;
        nextPoll[controllerId] = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, controller.PollingIntervalSeconds));
        var transportFailed = false;
        foreach (var meter in meters)
        {
            if (transportFailed) { meter.CommunicationStatus = "Failed"; continue; }
            try
            {
                decimal value;
                string communicationDetail;
                if (controller.CommunicationType == "BacnetIp")
                {
                    var result = await bacnet.ReadPresentValueAsync(controller, meter, ct);
                    value = result.Value;
                    communicationDetail = $"BACnet/IP {result.Address}; object {result.ObjectIdentifier}; Present_Value {result.RawValue}";
                }
                else if (controller.CommunicationType == "MBusTcp")
                {
                    (value, communicationDetail) = await ReadMBusAsync(mbus, controller, meter, ct);
                }
                else
                {
                    var count = ModbusValueConverter.RegisterCount(meter.DataType);
                    var result = await modbus.ReadHoldingRegistersAsync(controller, meter.StartingRegister, count, ct);
                    value = ModbusValueConverter.ConvertValue(result.Registers, meter.DataType, meter.WordOrder, meter.ScalingFactor) + meter.Offset;
                    communicationDetail = $"{TransportName(controller)} request {result.RequestHex}; response {result.ResponseHex}";
                }
                // After a meter replacement or a channel configuration change, the first value starts a new counter.
                if (meter.RebaseOnNextReading && value >= 0)
                {
                    db.MeterReadings.Add(new() { MeterId = meter.Id, RawValue = RawOf(meter, value), AccumulatedKwh = value, Timestamp = DateTimeOffset.UtcNow, Source = ReadingSource.Automatic, Quality = BillingRunService.BaselineQuality, Reason = "New starting value after meter replacement or configuration change" });
                    meter.RebaseOnNextReading = false; meter.LastReading = value; meter.LastReadingAt = DateTimeOffset.UtcNow; meter.CommunicationStatus = "Connected";
                    controller.LastSuccess = DateTimeOffset.UtcNow; controller.Condition = "Connected"; controller.Notes = $"Last {communicationDetail}";
                    continue;
                }
                // A cumulative counter decrease is suspect, not a replacement reading.
                // Preserve both the accepted value and its timestamp until it recovers.
                if (value < 0 || value < meter.LastReading && meter.LastReadingAt is not null)
                {
                    if (meter.CommunicationStatus != "Counter Review")
                        db.MeterReadings.Add(new() { MeterId = meter.Id, RawValue = RawOf(meter, value), AccumulatedKwh = value, Timestamp = DateTimeOffset.UtcNow, Source = ReadingSource.Automatic, Quality = "Review: counter decrease or negative value", RequiresReview = true });
                    meter.CommunicationStatus = "Counter Review";
                    controller.LastSuccess = DateTimeOffset.UtcNow;
                    controller.Condition = "Counter Review";
                    controller.Notes = $"Counter review for {meter.Name}: received {value}, retained {meter.LastReading}. {communicationDetail}";
                    continue;
                }
                meter.LastReading = value; meter.LastReadingAt = DateTimeOffset.UtcNow; meter.CommunicationStatus = "Connected"; controller.LastSuccess = DateTimeOffset.UtcNow; controller.Condition = "Connected"; controller.Notes = $"Last {communicationDetail}";
                await maintenance.Gate.WaitAsync(ct);
                try { if (maintenance.Generation == generation) await SaveHistoryIfDue(db, meter, ct); }
                finally { maintenance.Gate.Release(); }
            }
            catch (Exception ex) when (ex is MBusNoAnswerException or MBusProtocolException)
            {
                // The gateway works, only this meter is silent or sent an unusable answer: keep reading the others.
                meter.CommunicationStatus = "Failed"; controller.Notes = $"{meter.Name}: {ex.Message}";
                log.LogWarning("M-Bus meter {Meter} on {Controller}: {Message}", meter.Name, controller.Name, ex.Message);
            }
            catch (Exception ex) { meter.CommunicationStatus = "Failed"; controller.Condition = "Failed"; controller.Notes = ex.Message; transportFailed = true; log.LogWarning(ex, "{Transport} read failed for controller {Controller} on {Endpoint}; remaining channels skipped until next poll", TransportName(controller), controller.Name, controller.CommunicationType=="BacnetIp"?$"{controller.IpAddress}:{controller.BacnetUdpPort}":controller.CommunicationType is "ModbusTcp" or "MBusTcp"?$"{controller.IpAddress}:{controller.TcpPort}":controller.ComPort); }
        }
        await maintenance.Gate.WaitAsync(ct);
        try
        {
            // The database was restored while this controller was being read: drop these results.
            if (maintenance.Generation != generation) { log.LogInformation("Discarding poll results for controller {Controller}: database was restored during the poll", controllerId); return; }
            await db.SaveChangesAsync(ct);
        }
        finally { maintenance.Gate.Release(); }
    }

    static string TransportName(Controller controller) => controller.CommunicationType switch { "BacnetIp" => "BACnet/IP", "ModbusTcp" => "Modbus TCP/IP", "MBusTcp" => "M-Bus TCP", _ => "Modbus RTU" };

    /// <summary>Reads one M-Bus meter and returns its billing total (kWh or m³) multiplied by the meter's scaling factor.</summary>
    public static async Task<(decimal Value, string Detail)> ReadMBusAsync(IMBusService mbus, Controller controller, Meter meter, CancellationToken ct)
    {
        var result = await mbus.ReadAsync(controller, meter.StartingRegister, meter.MBusSecondaryAddress, ct);
        var record = MBusProtocol.SelectBillingRecord(result.Telegram, meter.UtilityType == UtilityType.Water ? MBusQuantity.Volume : MBusQuantity.Energy, meter.MBusRecord);
        var value = record.Value!.Value * meter.ScalingFactor + meter.Offset;
        return (value, $"M-Bus {result.AddressText} (ID {result.Telegram.SecondaryAddress}, {result.Telegram.Manufacturer}); record {record}");
    }

    /// <summary>The counter value the device sent, before scaling and the meter's offset.</summary>
    static ulong RawOf(Meter meter, decimal value) => (ulong)Math.Max(0, (value - meter.Offset) / (meter.ScalingFactor == 0 ? 1 : meter.ScalingFactor));

    static async Task SaveHistoryIfDue(ApplicationDbContext db, Meter meter, CancellationToken ct)
    {
        if (meter.LastReadingAt is null || meter.CommunicationStatus != "Connected") return;
        var last = await db.MeterReadings.Where(x => x.MeterId == meter.Id).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        // A newest reading dated in the future (PC clock moved back) must not stop the history.
        if (last is null || last.Timestamp > DateTimeOffset.UtcNow || DateTimeOffset.UtcNow - last.Timestamp >= TimeSpan.FromMinutes(15)) db.MeterReadings.Add(new() { MeterId = meter.Id, RawValue = RawOf(meter, meter.LastReading), AccumulatedKwh = meter.LastReading, Timestamp = DateTimeOffset.UtcNow, Source = ReadingSource.Automatic, Quality = "Good" });
    }
}
