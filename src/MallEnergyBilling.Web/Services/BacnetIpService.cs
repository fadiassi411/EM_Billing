using System.Collections.Concurrent;
using System.Globalization;
using System.IO.BACnet;
using MallEnergyBilling.Web.Models;

namespace MallEnergyBilling.Web.Services;

public sealed record BacnetReadResult(decimal Value, string Address, string ObjectIdentifier, string RawValue);

public interface IBacnetIpService
{
    Task<BacnetReadResult> ReadPresentValueAsync(Controller controller, Meter meter, CancellationToken cancellationToken);
}

public sealed class BacnetIpService : IBacnetIpService, IDisposable
{
    private readonly ConcurrentDictionary<string, BacnetClient> clients = new(StringComparer.OrdinalIgnoreCase);

    public async Task<BacnetReadResult> ReadPresentValueAsync(Controller controller, Meter meter, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(controller.BacnetLocalIp))
            throw new InvalidOperationException("Enter the main Watchdog PC local IP address for BACnet/IP.");
        if (string.IsNullOrWhiteSpace(controller.IpAddress))
            throw new InvalidOperationException("Enter the BACnet device IP address.");

        var key = $"{controller.BacnetLocalIp.Trim()}:{controller.BacnetUdpPort}";
        var client = clients.GetOrAdd(key, _ => CreateClient(controller));
        client.Timeout = Math.Max(100, controller.TimeoutMilliseconds);
        client.Retries = Math.Max(0, controller.RetryCount);

        var objectType = ParseObjectType(meter.BacnetObjectType);
        var addressText = controller.BacnetUdpPort == 47808
            ? controller.IpAddress.Trim()
            : $"{controller.IpAddress.Trim()}:{controller.BacnetUdpPort}";
        var address = new BacnetAddress(BacnetAddressTypes.IP, addressText);
        var values = await client.ReadPropertyAsync(
            address,
            new BacnetObjectId(objectType, checked((uint)meter.BacnetObjectInstance)),
            BacnetPropertyIds.PROP_PRESENT_VALUE,
            cancellationToken: cancellationToken);
        if (values.Count == 0) throw new InvalidDataException("The BACnet Present_Value response was empty.");
        var raw = values[0].Value ?? throw new InvalidDataException("The BACnet Present_Value response was empty.");
        decimal numeric;
        try { numeric = Convert.ToDecimal(raw, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        { throw new InvalidDataException($"BACnet Present_Value '{raw}' is not numeric.", ex); }
        var converted = numeric * meter.ScalingFactor;
        return new(converted, addressText, $"{meter.BacnetObjectType}:{meter.BacnetObjectInstance}", Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "");
    }

    private static BacnetClient CreateClient(Controller controller)
    {
        var transport = new BacnetIpUdpProtocolTransport(controller.BacnetUdpPort, localEndpointIp: controller.BacnetLocalIp.Trim());
        var client = new BacnetClient(transport)
        {
            Timeout = Math.Max(100, controller.TimeoutMilliseconds),
            Retries = Math.Max(0, controller.RetryCount)
        };
        client.Start();
        return client;
    }

    private static BacnetObjectTypes ParseObjectType(string value) => value switch
    {
        "AnalogInput" => BacnetObjectTypes.OBJECT_ANALOG_INPUT,
        "AnalogOutput" => BacnetObjectTypes.OBJECT_ANALOG_OUTPUT,
        "AnalogValue" => BacnetObjectTypes.OBJECT_ANALOG_VALUE,
        "LargeAnalogValue" => BacnetObjectTypes.OBJECT_LARGE_ANALOG_VALUE,
        "Accumulator" => BacnetObjectTypes.OBJECT_ACCUMULATOR,
        _ => throw new InvalidOperationException($"Unsupported BACnet object type '{value}'.")
    };

    public void Dispose()
    {
        foreach (var client in clients.Values) client.Dispose();
        clients.Clear();
    }
}
