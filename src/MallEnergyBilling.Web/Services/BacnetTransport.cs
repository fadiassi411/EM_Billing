using System.Collections.Concurrent;
using System.IO.BACnet;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MallEnergyBilling.Web.Models;

namespace MallEnergyBilling.Web.Services;

public sealed record BacnetInterface(string Name, string Address, bool IsUp, bool Special);
public sealed record BacnetObject(string Type, int Instance);
public sealed record BacnetRequest(BacnetObject Object, int Property, uint ArrayIndex = uint.MaxValue);
public sealed record BacnetDevice(int Instance, string Ip, int Port, int Vendor, string Name = "", string Model = "", string Status = "Online");

public interface IBacnetTransport
{
    Task<IReadOnlyList<object?>> ReadAsync(Controller config, BacnetRequest request, CancellationToken ct);
    Task<IReadOnlyDictionary<BacnetRequest, IReadOnlyList<object?>>> ReadMultipleAsync(Controller config, IReadOnlyList<BacnetRequest> requests, CancellationToken ct);
    Task<IReadOnlyList<BacnetDevice>> DiscoverAsync(Controller config, CancellationToken ct);
}

public static class BacnetNetwork
{
    public static IReadOnlyList<BacnetInterface> Interfaces() => NetworkInterface.GetAllNetworkInterfaces()
        .SelectMany(n => n.GetIPProperties().UnicastAddresses
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => new BacnetInterface(n.Name, a.Address.ToString(), n.OperationalStatus == OperationalStatus.Up,
                n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel ||
                new[] { "vpn", "virtual", "docker", "hyper-v", "vmware", "wsl", "tap", "tailscale" }
                    .Any(t => (n.Name + " " + n.Description).Contains(t, StringComparison.OrdinalIgnoreCase)))))
        .OrderBy(x => x.Special).ThenBy(x => x.Name).ToList();

    public static void Validate(Controller c, bool target = true)
    {
        if (!IPAddress.TryParse(c.BacnetLocalIp, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            throw new InvalidOperationException("Select a local IPv4 BACnet network interface.");
        if (!Interfaces().Any(x => x.Address == ip.ToString() && x.IsUp))
            throw new InvalidOperationException("The selected local network interface is disconnected or unavailable.");
        if (c.BacnetUdpPort is < 1 or > 65535 || c.TimeoutMilliseconds is < 100 or > 30000 || c.RetryCount is < 0 or > 10 || c.PollingIntervalSeconds is < 1 or > 3600)
            throw new InvalidOperationException("Invalid BACnet port, polling interval, timeout or retry count.");
        if (c.BacnetDeviceInstance is < 0 or > 4194302) throw new InvalidOperationException("Device instance must be 0–4194302, or blank before discovery.");
        if (target && (string.IsNullOrWhiteSpace(c.IpAddress) || Uri.CheckHostName(c.IpAddress.Trim()) is UriHostNameType.Unknown or UriHostNameType.IPv6))
            throw new InvalidOperationException("Enter a valid device IPv4 address or hostname.");
    }
}

// Only this adapter references the third-party stack. One socket per explicitly selected
// interface/port; requests have their own deadline/retry policy, never mutate shared options.
public sealed class BacnetTransport(ILoggerFactory logs) : IBacnetTransport, IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<BacnetClient>> clients = new();
    private readonly SemaphoreSlim requests = new(32);
    private readonly ILogger log = logs.CreateLogger<BacnetTransport>();
    private BacnetClient Client(Controller c)
    {
        BacnetNetwork.Validate(c, false);
        var key = $"{c.BacnetLocalIp}:{c.BacnetUdpPort}";
        var lazy = clients.GetOrAdd(key, _ => new Lazy<BacnetClient>(() =>
        {
            BacnetLogging.Factory = logs;
            var client = new BacnetClient(new BacnetIpUdpProtocolTransport(c.BacnetUdpPort, useExclusivePort: true, localEndpointIp: c.BacnetLocalIp)) { Timeout = 30000, Retries = 1 };
            try { client.Start(); log.LogInformation("BACnet service started on {Interface}", key); return client; }
            catch { client.Dispose(); throw; }
        }));
        try { return lazy.Value; }
        catch { clients.TryRemove(new KeyValuePair<string, Lazy<BacnetClient>>(key, lazy)); throw; }
    }
    private static async Task<BacnetAddress> Address(Controller c, CancellationToken ct)
    {
        BacnetNetwork.Validate(c);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(c.TimeoutMilliseconds);
        IPAddress[] addresses;
        try { addresses = await Dns.GetHostAddressesAsync(c.IpAddress.Trim(), deadline.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("BACnet device hostname lookup timed out."); }
        var ip = addresses.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork) ?? throw new InvalidOperationException("Device hostname has no IPv4 address.");
        return new BacnetAddress(BacnetAddressTypes.IP, $"{ip}:{c.BacnetUdpPort}");
    }
    public static BacnetObjectTypes ObjectType(string value) => value switch
    {
        "AnalogInput" => BacnetObjectTypes.OBJECT_ANALOG_INPUT, "AnalogOutput" => BacnetObjectTypes.OBJECT_ANALOG_OUTPUT,
        "AnalogValue" => BacnetObjectTypes.OBJECT_ANALOG_VALUE, "BinaryInput" => BacnetObjectTypes.OBJECT_BINARY_INPUT,
        "BinaryOutput" => BacnetObjectTypes.OBJECT_BINARY_OUTPUT, "BinaryValue" => BacnetObjectTypes.OBJECT_BINARY_VALUE,
        "MultiStateInput" => BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT, "MultiStateOutput" => BacnetObjectTypes.OBJECT_MULTI_STATE_OUTPUT,
        "MultiStateValue" => BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE, "Accumulator" => BacnetObjectTypes.OBJECT_ACCUMULATOR,
        "LargeAnalogValue" => BacnetObjectTypes.OBJECT_LARGE_ANALOG_VALUE, "Device" => BacnetObjectTypes.OBJECT_DEVICE,
        _ when int.TryParse(value, out var type) && type is >= 0 and <= 1023 => (BacnetObjectTypes)type,
        _ => throw new InvalidOperationException("Unsupported BACnet object type.")
    };
    private static BacnetObjectId Id(BacnetObject o) => new(ObjectType(o.Type), checked((uint)o.Instance));
    private static object? Unwrap(BacnetValue v) => v.Value switch
    {
        BacnetObjectId id => new BacnetObject(BacnetIpService.ObjectTypes.FirstOrDefault(x => ObjectType(x) == id.type) ?? (id.type == BacnetObjectTypes.OBJECT_DEVICE ? "Device" : ((int)id.type).ToString()), (int)id.instance),
        BacnetError error => throw new IOException($"BACnet error: {error}"),
        BacnetBitString bits => bits.ToString(),
        _ => v.Value
    };
    private async Task<T> Execute<T>(Controller c, Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        await requests.WaitAsync(ct);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(c.TimeoutMilliseconds);
                try { return await operation(deadline.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                { if (attempt >= c.RetryCount) throw new TimeoutException("No BACnet response before the configured timeout."); }
                catch (TimeoutException) when (attempt < c.RetryCount) { }
            }
        }
        finally { requests.Release(); }
    }
    public async Task<IReadOnlyList<object?>> ReadAsync(Controller c, BacnetRequest r, CancellationToken ct)
    {
        var client = Client(c); var address = await Address(c, ct);
        var values = await Execute(c, token => client.ReadPropertyAsync(address, Id(r.Object), (BacnetPropertyIds)r.Property, arrayIndex: r.ArrayIndex, cancellationToken: token), ct);
        return values.Select(Unwrap).ToArray();
    }
    public async Task<IReadOnlyDictionary<BacnetRequest, IReadOnlyList<object?>>> ReadMultipleAsync(Controller c, IReadOnlyList<BacnetRequest> rr, CancellationToken ct)
    {
        var client = Client(c); var address = await Address(c, ct);
        var specs = rr.GroupBy(x => x.Object).Select(g => new BacnetReadAccessSpecification(Id(g.Key), g.Select(r => new BacnetPropertyReference((uint)r.Property, r.ArrayIndex)).ToArray())).ToArray();
        var result = await Execute(c, token => client.ReadPropertyMultipleAsync(address, specs, cancellationToken: token), ct);
        var values = new Dictionary<BacnetRequest, IReadOnlyList<object?>>();
        foreach (var r in rr)
        {
            var row = result.FirstOrDefault(x => x.objectIdentifier.Equals(Id(r.Object)));
            var property = row.values?.FirstOrDefault(x => x.property.propertyIdentifier == r.Property && x.property.propertyArrayIndex == r.ArrayIndex);
            if (property?.value is null) continue;
            try { values[r] = property.Value.value.Select(Unwrap).ToArray(); }
            catch (IOException) { /* Per-property error: application retries this property individually. */ }
        }
        return values;
    }
    public async Task<IReadOnlyList<BacnetDevice>> DiscoverAsync(Controller c, CancellationToken ct)
    {
        var client = Client(c); var found = new ConcurrentDictionary<string, BacnetDevice>();
        void OnIam(BacnetClient sender, BacnetAddress address, uint instance, uint max, BacnetSegmentations segmentation, ushort vendor)
        {
            if (address.adr is not { Length: 6 } || instance > 4194302) return;
            var ip = new IPAddress(address.adr.Take(4).ToArray()).ToString();
            var port = (address.adr[4] << 8) | address.adr[5];
            found[$"{ip}:{port}/{instance}"] = new((int)instance, ip, port, vendor);
            log.LogDebug("BACnet I-Am received: {Ip}, instance {Instance}", ip, instance);
        }
        client.OnIam += OnIam;
        try
        {
            log.LogInformation("BACnet Who-Is on {Interface}", c.BacnetLocalIp);
            client.WhoIs();
            await Task.Delay(Math.Clamp(c.TimeoutMilliseconds * (c.RetryCount + 1), 1000, 10000), ct);
            return found.Values.OrderBy(x => x.Instance).Take(256).ToArray();
        }
        finally { client.OnIam -= OnIam; }
    }
    public void Dispose()
    {
        foreach (var c in clients.Values.Where(x => x.IsValueCreated)) c.Value.Dispose();
        clients.Clear(); requests.Dispose();
    }
}
