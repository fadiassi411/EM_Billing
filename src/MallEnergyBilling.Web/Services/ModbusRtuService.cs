using System.Collections.Concurrent;
using System.IO.Ports;
using MallEnergyBilling.Web.Models;
using ControllerModel = MallEnergyBilling.Web.Models.Controller;

namespace MallEnergyBilling.Web.Services;

public sealed record ModbusReadResult(ushort[] Registers, string RequestHex, string ResponseHex);

public interface IModbusRtuService
{
    Task<ModbusReadResult> ReadHoldingRegistersAsync(ControllerModel controller, int startAddress, ushort count, CancellationToken cancellationToken = default);
}

public sealed class ModbusRtuService(ILogger<ModbusRtuService> logger) : IModbusRtuService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> portLocks = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ModbusReadResult> ReadHoldingRegistersAsync(ControllerModel c, int startAddress, ushort count, CancellationToken ct = default)
    {
        if (startAddress is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(startAddress));
        if (count is < 1 or > 125) throw new ArgumentOutOfRangeException(nameof(count));
        var gate = portLocks.GetOrAdd(c.ComPort, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            Exception? last = null;
            for (var attempt = 0; attempt <= c.RetryCount; attempt++)
            {
                try { return await Task.Run(() => ReadOnce(c, startAddress, count), ct); }
                catch (Exception ex) when (attempt < c.RetryCount) { last = ex; logger.LogWarning(ex, "Modbus attempt {Attempt} failed on {Port}", attempt + 1, c.ComPort); await Task.Delay(100, ct); }
            }
            throw last ?? new IOException("Modbus RTU read failed.");
        }
        finally { gate.Release(); }
    }

    private static ModbusReadResult ReadOnce(ControllerModel c, int startAddress, ushort count)
    {
        var request = new byte[8]; request[0] = c.SlaveAddress; request[1] = 3; request[2] = (byte)(startAddress >> 8); request[3] = (byte)startAddress; request[4] = (byte)(count >> 8); request[5] = (byte)count;
        var crc = Crc16(request.AsSpan(0, 6)); request[6] = (byte)crc; request[7] = (byte)(crc >> 8);
        using var port = new SerialPort(c.ComPort, c.BaudRate, Enum.Parse<Parity>(c.Parity, true), c.DataBits, c.StopBits == 2 ? StopBits.Two : StopBits.One)
        { ReadTimeout = c.TimeoutMilliseconds, WriteTimeout = c.TimeoutMilliseconds, Handshake = Handshake.None, DtrEnable = false, RtsEnable = false };
        port.Open(); port.DiscardInBuffer(); port.DiscardOutBuffer(); port.Write(request, 0, request.Length);
        var header = ReadExact(port, 3);
        if (header[0] != c.SlaveAddress) throw new IOException($"Unexpected slave {header[0]}; expected {c.SlaveAddress}.");
        if ((header[1] & 0x80) != 0) throw new IOException($"PLC returned Modbus exception code {header[2]}.");
        if (header[1] != 3) throw new IOException($"Unexpected function {header[1]}.");
        if (header[2] != count * 2) throw new IOException($"Unexpected byte count {header[2]}; expected {count * 2}.");
        var tail = ReadExact(port, header[2] + 2); var response = header.Concat(tail).ToArray();
        var receivedCrc = (ushort)(response[^2] | response[^1] << 8); var calculatedCrc = Crc16(response.AsSpan(0, response.Length - 2));
        if (receivedCrc != calculatedCrc) throw new IOException($"CRC mismatch: received {receivedCrc:X4}, calculated {calculatedCrc:X4}.");
        var registers = new ushort[count]; for (var n = 0; n < count; n++) registers[n] = (ushort)(response[3 + n * 2] << 8 | response[4 + n * 2]);
        return new(registers, Convert.ToHexString(request), Convert.ToHexString(response));
    }

    private static byte[] ReadExact(SerialPort port, int length)
    {
        var data = new byte[length]; var offset = 0;
        while (offset < length) { var read = port.Read(data, offset, length - offset); if (read <= 0) throw new TimeoutException("No Modbus response received."); offset += read; }
        return data;
    }

    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var value in data) { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1); }
        return crc;
    }
}

public static class ModbusValueConverter
{
    public static ushort RegisterCount(RegisterDataType type) => type switch { RegisterDataType.UInt16 or RegisterDataType.Int16 => 1, RegisterDataType.UInt32 or RegisterDataType.Int32 or RegisterDataType.Float32 => 2, RegisterDataType.UInt64 => 4, _ => throw new NotSupportedException() };
    public static decimal ConvertValue(ushort[] input, RegisterDataType type, WordOrder order, decimal scale)
    {
        var words = order == WordOrder.LowHigh ? input.Reverse().ToArray() : input;
        return type switch
        {
            RegisterDataType.UInt16 => input[0] * scale,
            RegisterDataType.Int16 => (short)input[0] * scale,
            RegisterDataType.UInt32 => (((uint)words[0] << 16) | words[1]) * scale,
            RegisterDataType.Int32 => unchecked((int)(((uint)words[0] << 16) | words[1])) * scale,
            RegisterDataType.Float32 => (decimal)BitConverter.Int32BitsToSingle(unchecked((int)(((uint)words[0] << 16) | words[1]))) * scale,
            RegisterDataType.UInt64 => Combine64(words) * scale,
            _ => throw new NotSupportedException($"Unsupported register type {type}.")
        };
    }
    private static ulong Combine64(ushort[] w) => ((ulong)w[0] << 48) | ((ulong)w[1] << 32) | ((ulong)w[2] << 16) | w[3];
}
