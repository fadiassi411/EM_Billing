using System.Collections.Concurrent;
using System.Globalization;
using MallEnergyBilling.Web.Models;

namespace MallEnergyBilling.Web.Services;

public sealed record BacnetReadResult(decimal Value, string Address, string ObjectIdentifier, string RawValue);
public sealed record BacnetPointResult(int PointId, decimal? Raw, decimal? Value, string Quality, string Error, DateTimeOffset Timestamp);
public sealed record BacnetObjectInfo(string Type, int Instance, string Name, string Description, string Value, string Unit, string Reliability, string Status);

public interface IBacnetIpService
{
    Task<BacnetReadResult> ReadPresentValueAsync(Controller controller, Meter meter, CancellationToken ct);
    Task<BacnetDevice> TestAsync(Controller controller, CancellationToken ct);
    Task<IReadOnlyList<BacnetDevice>> DiscoverAsync(Controller controller, CancellationToken ct);
    Task<IReadOnlyList<BacnetObjectInfo>> BrowseAsync(Controller controller, CancellationToken ct);
    Task<IReadOnlyList<BacnetPointResult>> ReadPointsAsync(Controller controller, IReadOnlyList<BacnetPoint> points, CancellationToken ct);
}

public sealed class BacnetIpService(IBacnetTransport transport, ILogger<BacnetIpService> log) : IBacnetIpService
{
    public static readonly string[] ObjectTypes = ["AnalogInput", "AnalogOutput", "AnalogValue", "BinaryInput", "BinaryOutput", "BinaryValue", "MultiStateInput", "MultiStateOutput", "MultiStateValue", "Accumulator", "LargeAnalogValue"];
    private readonly ConcurrentDictionary<string, DateTimeOffset> rpmUnsupported = new();
    public static void ValidatePoint(BacnetPoint p)
    {
        if (!ObjectTypes.Contains(p.ObjectType) || p.ObjectInstance is < 0 or > 4194302 || p.PropertyId is < 0 or > 4194303)
            throw new InvalidOperationException("Select a supported object type, instance 0–4194302 and valid property identifier.");
        if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 100 || p.Unit.Length > 50 || !Enum.IsDefined(p.Measurement) || p.Scale is < -1000000 or > 1000000 || p.Offset is < -1000000000000 or > 1000000000000)
            throw new InvalidOperationException("Invalid point name, measurement, unit, scaling or offset.");
        if (p.Measurement == MeasurementType.TotalImportEnergy && (!p.BillingConfirmed || p.Unit != "kWh" || p.Scale <= 0 || p.PropertyId != 85))
            throw new InvalidOperationException("Explicitly confirm the cumulative import-energy Present_Value point, with final unit kWh and positive scaling, before mapping it to billing.");
    }
    public static decimal Numeric(object? value) => value switch
    {
        bool v => v ? 1 : 0, byte v => v, sbyte v => v, short v => v, ushort v => v,
        int v => v, uint v => v, long v => v, ulong v => v, decimal v => v,
        float v when float.IsFinite(v) => checked((decimal)v), double v when double.IsFinite(v) => checked((decimal)v),
        Enum v => Convert.ToDecimal(v, CultureInfo.InvariantCulture),
        _ => throw new InvalidDataException("BACnet returned a null, non-numeric or non-finite value.")
    };
    public static string Error(Exception ex) => ex switch
    {
        TimeoutException => "Timeout: no BACnet response. Check device address, UDP port and local interface.",
        System.Net.Sockets.SocketException => "UDP port or network unavailable. Check interface, firewall and other BACnet applications.",
        InvalidOperationException => ex.Message, InvalidDataException => ex.Message,
        _ => "BACnet service error: object/property may be unavailable, rejected, aborted or access denied. See diagnostic log."
    };
    private static BacnetRequest Request(BacnetPoint p) => new(new(p.ObjectType, p.ObjectInstance), p.PropertyId);
    private static string Text(IReadOnlyList<object?>? values) => values is null ? "" : string.Join(", ", values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)));
    private async Task<string> Optional(Controller c, BacnetObject obj, int property, CancellationToken ct)
    {
        try { return Text(await transport.ReadAsync(c, new(obj, property), ct)); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { log.LogDebug(ex, "Optional BACnet property {Property} unavailable on {Object}", property, obj); return ""; }
    }
    public async Task<BacnetDevice> TestAsync(Controller c, CancellationToken ct)
    {
        if (c.BacnetDeviceInstance is null) throw new InvalidOperationException("Enter or discover the device instance; zero is a valid instance.");
        var obj = new BacnetObject("Device", c.BacnetDeviceInstance.Value);
        var identity = await transport.ReadAsync(c, new(obj, 75), ct);
        if (identity.Count != 1 || identity[0] is not BacnetObject { Type: "Device" } actual || actual.Instance != obj.Instance)
            throw new InvalidOperationException("Device Instance mismatch in BACnet Device Object response.");
        var name = await Optional(c, obj, 77, ct); var model = await Optional(c, obj, 70, ct); var vendor = await Optional(c, obj, 120, ct);
        log.LogDebug("BACnet connection test succeeded: {Ip}, device {Instance}", c.IpAddress, obj.Instance);
        return new(obj.Instance, c.IpAddress, c.BacnetUdpPort, int.TryParse(vendor, out var v) ? v : 0, name, model);
    }
    public async Task<IReadOnlyList<BacnetDevice>> DiscoverAsync(Controller c, CancellationToken ct)
    {
        var devices = await transport.DiscoverAsync(c, ct); var results = new ConcurrentBag<BacnetDevice>();
        await Parallel.ForEachAsync(devices, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (d, token) =>
        {
            var config = new Controller { IpAddress = d.Ip, BacnetDeviceInstance = d.Instance, BacnetUdpPort = d.Port, BacnetLocalIp = c.BacnetLocalIp, TimeoutMilliseconds = c.TimeoutMilliseconds, RetryCount = 0 };
            results.Add(d with { Name = await Optional(config, new("Device", d.Instance), 77, token), Model = await Optional(config, new("Device", d.Instance), 70, token) });
        });
        return results.OrderBy(x => x.Instance).ToArray();
    }
    public async Task<IReadOnlyList<BacnetObjectInfo>> BrowseAsync(Controller c, CancellationToken ct)
    {
        await TestAsync(c, ct); var device = new BacnetObject("Device", c.BacnetDeviceInstance!.Value);
        IReadOnlyList<object?> list;
        try { list = await transport.ReadAsync(c, new(device, 76), ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Reading BACnet Object_List by array index");
            var countValues = await transport.ReadAsync(c, new(device, 76, 0), ct);
            var count = checked((int)Numeric(countValues.SingleOrDefault()));
            if (count is < 0 or > 4096) throw new InvalidOperationException("Object list exceeds 4096 objects. Use manual point entry.");
            var items = new List<object?>();
            for (uint i = 1; i <= count; i++) items.AddRange(await transport.ReadAsync(c, new(device, 76, i), ct));
            list = items;
        }
        if (list.Count > 4096) throw new InvalidOperationException("Object list exceeds 4096 objects. Use manual point entry.");
        var result = new List<BacnetObjectInfo>();
        foreach (var obj in list.OfType<BacnetObject>().Distinct())
        {
            var requests = new[] { 77, 28, 85, 117, 103, 111 }.Select(p => new BacnetRequest(obj, p)).ToArray();
            IReadOnlyDictionary<BacnetRequest, IReadOnlyList<object?>> batch = new Dictionary<BacnetRequest, IReadOnlyList<object?>>();
            try { batch = await transport.ReadMultipleAsync(c, requests, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { log.LogDebug(ex, "BACnet metadata RPM unavailable"); }
            var fields = new List<string>();
            foreach (var request in requests) fields.Add(batch.TryGetValue(request, out var value) ? Text(value) : await Optional(c, obj, request.Property, ct));
            result.Add(new(obj.Type, obj.Instance, fields[0], fields[1], fields[2], UnitName(fields[3]), fields[4], fields[5]));
        }
        return result;
    }
    public static string UnitName(string unit) => unit switch { "3" => "A", "62" => "°C", "95" => "no-units", "98" => "%", "27" => "Hz", "48" => "kW", "9" => "kVA", "12" => "kvar", "19" => "kWh", "204" => "kvarh", "5" => "V", "47" => "W", _ => unit };
    public async Task<IReadOnlyList<BacnetPointResult>> ReadPointsAsync(Controller c, IReadOnlyList<BacnetPoint> points, CancellationToken ct)
    {
        var output = new List<BacnetPointResult>(); var valid = new List<BacnetPoint>();
        foreach (var p in points.Where(x => x.Enabled))
        {
            try { ValidatePoint(p); valid.Add(p); }
            catch (Exception ex) { output.Add(new(p.Id, null, null, "Configuration Error", Error(ex), DateTimeOffset.UtcNow)); }
        }
        var endpoint = $"{c.BacnetLocalIp}/{c.IpAddress}:{c.BacnetUdpPort}";
        foreach (var chunk in valid.Chunk(8))
        {
            var requests = chunk.SelectMany(p => new[] { Request(p), new BacnetRequest(Request(p).Object, 103), new BacnetRequest(Request(p).Object, 111) }).Distinct().ToArray();
            IReadOnlyDictionary<BacnetRequest, IReadOnlyList<object?>> batch = new Dictionary<BacnetRequest, IReadOnlyList<object?>>();
            if (!rpmUnsupported.TryGetValue(endpoint, out var until) || until < DateTimeOffset.UtcNow)
            {
                try { batch = await transport.ReadMultipleAsync(c, requests, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { rpmUnsupported[endpoint] = DateTimeOffset.UtcNow.AddMinutes(10); log.LogDebug(ex, "BACnet RPM failed; using individual reads for {Endpoint}", endpoint); }
            }
            foreach (var p in chunk)
            {
                try
                {
                    var request = Request(p);
                    var values = batch.TryGetValue(request, out var cached) ? cached : await transport.ReadAsync(c, request, ct);
                    if (values.Count != 1) throw new InvalidDataException("BACnet point must return one numerical value.");
                    var raw = Numeric(values[0]); var value = checked(raw * p.Scale + p.Offset);
                    var reliability = batch.TryGetValue(new(request.Object, 103), out var r) ? Text(r) : await Optional(c, request.Object, 103, ct);
                    var flags = batch.TryGetValue(new(request.Object, 111), out var f) ? Text(f) : await Optional(c, request.Object, 111, ct);
                    var bad = reliability.Length > 0 && reliability is not ("0" or "NO_FAULT_DETECTED") || flags.Contains('1');
                    output.Add(new(p.Id, raw, value, bad ? "Bad" : "Good", bad ? $"Reliability {reliability}; Status_Flags {flags}" : "", DateTimeOffset.UtcNow));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { log.LogDebug(ex, "BACnet object {Type}:{Instance} read failure", p.ObjectType, p.ObjectInstance); output.Add(new(p.Id, null, null, ex is TimeoutException ? "Timeout" : "Bad", Error(ex), DateTimeOffset.UtcNow)); }
            }
        }
        return output;
    }
    public async Task<BacnetReadResult> ReadPresentValueAsync(Controller c, Meter m, CancellationToken ct)
    {
        var point = new BacnetPoint { Name = m.Name, ObjectType = m.BacnetObjectType, ObjectInstance = m.BacnetObjectInstance, Scale = m.ScalingFactor };
        var result = (await ReadPointsAsync(c, [point], ct)).Single();
        if (result.Quality != "Good") throw new InvalidDataException(result.Error);
        return new(result.Value!.Value, $"{c.IpAddress}:{c.BacnetUdpPort}", $"{point.ObjectType}:{point.ObjectInstance}", result.Raw!.Value.ToString(CultureInfo.InvariantCulture));
    }
}
