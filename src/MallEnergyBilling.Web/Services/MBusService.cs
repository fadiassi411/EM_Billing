using System.Collections.Concurrent;
using System.Net.Sockets;
using ControllerModel = MallEnergyBilling.Web.Models.Controller;

namespace MallEnergyBilling.Web.Services;

public sealed record MBusReadResult(MBusTelegram Telegram, string AddressText, string ResponseHex);

/// <summary>The gateway was reached, but this one meter did not answer. Other meters on the bus can still be read.</summary>
public sealed class MBusNoAnswerException(string message) : IOException(message);

public interface IMBusService
{
    /// <summary>Reads one meter. A valid 8-digit <paramref name="secondaryAddress"/> takes priority over the primary address.</summary>
    Task<MBusReadResult> ReadAsync(ControllerModel controller, int primaryAddress, string? secondaryAddress, CancellationToken ct = default);
}

/// <summary>
/// Wired M-Bus through a transparent M-Bus to Ethernet gateway (raw M-Bus frames over a TCP socket).
/// The gateway itself is set to the bus speed (usually 2400 baud).
/// </summary>
public sealed class MBusService(ILogger<MBusService> logger) : IMBusService
{
    readonly ConcurrentDictionary<string, SemaphoreSlim> gatewayLocks = new(StringComparer.OrdinalIgnoreCase);

    public async Task<MBusReadResult> ReadAsync(ControllerModel c, int primaryAddress, string? secondaryAddress, CancellationToken ct = default)
    {
        var secondary = MBusProtocol.NormalizeSecondaryAddress(secondaryAddress);
        if (secondary is null && primaryAddress is < 0 or > 250) throw new ArgumentOutOfRangeException(nameof(primaryAddress), "M-Bus primary address must be 0–250.");
        var endpoint = $"{c.IpAddress}:{c.TcpPort}";
        var gate = gatewayLocks.GetOrAdd(endpoint, _ => new SemaphoreSlim(1, 1)); // one conversation on the bus at a time
        await gate.WaitAsync(ct);
        try
        {
            Exception? last = null;
            for (var attempt = 0; attempt <= c.RetryCount; attempt++)
            {
                try { return await ReadOnceAsync(c, primaryAddress, secondary, ct); }
                catch (Exception ex) when (attempt < c.RetryCount && ex is not OperationCanceledException)
                {
                    last = ex; logger.LogWarning(ex, "M-Bus attempt {Attempt} failed on {Endpoint}", attempt + 1, endpoint);
                    await Task.Delay(200, ct);
                }
            }
            throw last ?? new IOException("M-Bus read failed.");
        }
        finally { gate.Release(); }
    }

    static async Task<MBusReadResult> ReadOnceAsync(ControllerModel c, int primary, string? secondary, CancellationToken ct)
    {
        using var client = new TcpClient { NoDelay = true };
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connect.CancelAfter(Math.Max(1000, c.TimeoutMilliseconds));
            try { await client.ConnectAsync(c.IpAddress, c.TcpPort, connect.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException($"The M-Bus gateway {c.IpAddress}:{c.TcpPort} did not accept a connection."); }
        }
        await using var stream = client.GetStream();
        string addressText;
        byte address;
        if (secondary is not null)
        {
            addressText = $"secondary address {secondary}";
            await SendAsync(stream, MBusProtocol.SndNke(MBusProtocol.SecondaryAddressingAddress), ct);
            await TryReadFrameAsync(stream, c.TimeoutMilliseconds, ct); // deselects a previously selected meter; an answer is optional
            await SendAsync(stream, MBusProtocol.SelectSecondary(secondary), ct);
            var selected = await TryReadFrameAsync(stream, c.TimeoutMilliseconds, ct);
            if (selected is not [MBusProtocol.Ack]) throw new MBusNoAnswerException($"No M-Bus meter answered to {addressText}. Check the 8-digit ID on the meter label.");
            address = MBusProtocol.SecondaryAddressingAddress;
        }
        else
        {
            addressText = $"primary address {primary}";
            address = (byte)primary;
            await SendAsync(stream, MBusProtocol.SndNke(address), ct);
            await TryReadFrameAsync(stream, c.TimeoutMilliseconds, ct); // most meters acknowledge with 0xE5; some stay silent
        }
        await SendAsync(stream, MBusProtocol.ReqUd2(address), ct);
        var frame = await TryReadFrameAsync(stream, Math.Max(c.TimeoutMilliseconds, 1500), ct);
        if (frame is [MBusProtocol.Ack]) frame = await TryReadFrameAsync(stream, Math.Max(c.TimeoutMilliseconds, 1500), ct); // a late acknowledgement of the reset
        if (frame is null) throw new MBusNoAnswerException($"No M-Bus meter answered at {addressText}.");
        return new(MBusProtocol.ParseResponse(frame), addressText, Convert.ToHexString(frame));
    }

    static async Task SendAsync(NetworkStream stream, byte[] frame, CancellationToken ct)
    {
        await stream.WriteAsync(frame, ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>Reads one complete frame (0xE5, short or long frame). Returns null when nothing arrives in time.</summary>
    static async Task<byte[]?> TryReadFrameAsync(NetworkStream stream, int timeoutMilliseconds, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutMilliseconds);
        var buffer = new List<byte>(300);
        var chunk = new byte[512];
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(chunk, timeout.Token);
                if (read == 0) throw new IOException("The M-Bus gateway closed the connection.");
                buffer.AddRange(chunk.AsSpan(0, read));
                while (buffer.Count > 0)
                {
                    // Ignore line noise before a start byte.
                    var skip = buffer.FindIndex(b => b is MBusProtocol.Ack or 0x10 or 0x68);
                    if (skip < 0) { buffer.Clear(); break; }
                    if (skip > 0) buffer.RemoveRange(0, skip);
                    int length;
                    try { length = MBusProtocol.CompleteFrameLength(buffer.ToArray()); }
                    catch (MBusProtocolException) { buffer.RemoveAt(0); continue; }
                    if (length > 0) return buffer.GetRange(0, length).ToArray();
                    break; // wait for the rest of the frame
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
    }
}
