using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace MallEnergyBilling.Web.Services;

/// <summary>What an M-Bus data record measures, after converting to the units Watchdog bills in.</summary>
public enum MBusQuantity { Other, Energy, Volume, Power, VolumeFlow, Temperature, DateTime, Identification }

/// <summary>One decoded data record of an M-Bus RSP_UD telegram (EN 13757-3).</summary>
public sealed record MBusRecord(
    int Index,
    MBusQuantity Quantity,
    string Description,
    decimal? Value,
    string Unit,
    int StorageNumber,
    int Tariff,
    int SubUnit,
    string Function,
    string RawHex)
{
    /// <summary>A billing total: the current (storage 0), main tariff, main unit, instantaneous value.</summary>
    public bool IsCurrentTotal => StorageNumber == 0 && Tariff == 0 && SubUnit == 0 && Function == "Instantaneous" && Value is not null;
    public override string ToString() => $"#{Index} {Description}: {(Value is null ? "—" : Value.Value.ToString("0.######", CultureInfo.InvariantCulture))} {Unit}"
        + (StorageNumber != 0 ? $" (storage {StorageNumber})" : "") + (Tariff != 0 ? $" (tariff {Tariff})" : "") + (SubUnit != 0 ? $" (sub-unit {SubUnit})" : "")
        + (Function != "Instantaneous" ? $" ({Function.ToLowerInvariant()})" : "");
}

/// <summary>A decoded RSP_UD answer.</summary>
public sealed record MBusTelegram(byte Address, string SecondaryAddress, string Manufacturer, byte Version, string Medium, byte AccessNumber, byte Status, IReadOnlyList<MBusRecord> Records, bool MoreRecordsFollow);

public sealed class MBusProtocolException(string message) : IOException(message);

/// <summary>
/// Frame building and decoding for wired M-Bus (EN 13757-2 link layer, EN 13757-3 application layer).
/// Only what a billing master needs: SND_NKE, secondary-address selection, REQ_UD2 and RSP_UD decoding.
/// </summary>
public static class MBusProtocol
{
    public const byte Ack = 0xE5;
    public const byte SecondaryAddressingAddress = 0xFD;
    public const byte BroadcastAddress = 0xFF;

    public static byte[] ShortFrame(byte control, byte address) => [0x10, control, address, (byte)(control + address), 0x16];
    /// <summary>SND_NKE: resets the link of one meter. The meter answers with a single 0xE5.</summary>
    public static byte[] SndNke(byte address) => ShortFrame(0x40, address);
    /// <summary>REQ_UD2: asks for class-2 (consumption) data. FCB is set on alternate requests.</summary>
    public static byte[] ReqUd2(byte address, bool fcb = true) => ShortFrame(fcb ? (byte)0x7B : (byte)0x5B, address);

    public static byte[] LongFrame(byte control, byte address, byte ci, ReadOnlySpan<byte> data)
    {
        var length = 3 + data.Length;
        if (length > 255) throw new ArgumentException("M-Bus frame is too long.", nameof(data));
        var frame = new byte[length + 6];
        frame[0] = 0x68; frame[1] = (byte)length; frame[2] = (byte)length; frame[3] = 0x68;
        frame[4] = control; frame[5] = address; frame[6] = ci; data.CopyTo(frame.AsSpan(7));
        byte sum = 0; for (var i = 4; i < 4 + length; i++) sum += frame[i];
        frame[^2] = sum; frame[^1] = 0x16;
        return frame;
    }

    /// <summary>SND_UD to 0xFD with CI 0x52: selects the meter whose 8-digit identification number matches.</summary>
    public static byte[] SelectSecondary(string identification)
    {
        var id = NormalizeSecondaryAddress(identification) ?? throw new ArgumentException("The M-Bus secondary address must be 8 digits.", nameof(identification));
        var data = new byte[8];
        for (var i = 0; i < 4; i++) data[i] = (byte)((id[6 - 2 * i] - '0') << 4 | (id[7 - 2 * i] - '0'));
        data[4] = 0xFF; data[5] = 0xFF; data[6] = 0xFF; data[7] = 0xFF; // any manufacturer, version, medium
        return LongFrame(0x53, SecondaryAddressingAddress, 0x52, data);
    }

    /// <summary>Returns the 8-digit identification number, or null if the text is not one.</summary>
    public static string? NormalizeSecondaryAddress(string? text)
    {
        var digits = new string((text ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        return digits.Length == 8 && digits.All(char.IsAsciiDigit) ? digits : null;
    }

    /// <summary>Length of a complete frame at the start of <paramref name="buffer"/>, 0 if more bytes are needed.</summary>
    public static int CompleteFrameLength(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length == 0) return 0;
        if (buffer[0] == Ack) return 1;
        if (buffer[0] == 0x10) return buffer.Length >= 5 ? 5 : 0;
        if (buffer[0] != 0x68) throw new MBusProtocolException($"Unexpected start byte 0x{buffer[0]:X2}.");
        if (buffer.Length < 4) return 0;
        if (buffer[1] != buffer[2] || buffer[3] != 0x68) throw new MBusProtocolException("Invalid M-Bus long frame header.");
        var total = buffer[1] + 6;
        return buffer.Length >= total ? total : 0;
    }

    /// <summary>Checks a long frame and decodes its RSP_UD contents.</summary>
    public static MBusTelegram ParseResponse(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 9 || frame[0] != 0x68 || frame[3] != 0x68 || frame[1] != frame[2]) throw new MBusProtocolException("The meter did not answer with an M-Bus long frame.");
        var length = frame[1];
        if (frame.Length != length + 6) throw new MBusProtocolException($"M-Bus frame length {frame.Length} does not match the header ({length + 6}).");
        if (frame[^1] != 0x16) throw new MBusProtocolException("M-Bus frame stop byte is missing.");
        byte sum = 0; for (var i = 4; i < 4 + length; i++) sum += frame[i];
        if (sum != frame[^2]) throw new MBusProtocolException($"M-Bus checksum mismatch: received {frame[^2]:X2}, calculated {sum:X2}.");
        var control = frame[4]; var address = frame[5]; var ci = frame[6];
        if ((control & 0x4F) != 0x08) throw new MBusProtocolException($"Unexpected M-Bus control field 0x{control:X2}; expected RSP_UD.");
        var body = frame.Slice(7, length - 3);
        string secondary = "", manufacturer = "", medium = "";
        byte version = 0, access = 0, status = 0;
        int position;
        switch (ci)
        {
            case 0x72: case 0x76: // variable data, long header
                if (body.Length < 12) throw new MBusProtocolException("M-Bus header is incomplete.");
                secondary = Bcd(body[..4]).ToString("00000000", CultureInfo.InvariantCulture);
                manufacturer = Manufacturer(BinaryPrimitives.ReadUInt16LittleEndian(body[4..6]));
                version = body[6]; medium = MediumName(body[7]); access = body[8]; status = body[9];
                position = 12; break;
            case 0x7A: // short header
                if (body.Length < 4) throw new MBusProtocolException("M-Bus header is incomplete.");
                access = body[0]; status = body[1]; position = 4; break;
            case 0x78: position = 0; break;
            default: throw new MBusProtocolException($"Unsupported M-Bus CI field 0x{ci:X2}. Only variable data responses are supported.");
        }
        var (records, more) = ParseRecords(body[position..]);
        return new(address, secondary, manufacturer, version, medium, access, status, records, more);
    }

    static (List<MBusRecord>, bool) ParseRecords(ReadOnlySpan<byte> data)
    {
        var records = new List<MBusRecord>();
        var i = 0; var more = false;
        while (i < data.Length)
        {
            var start = i;
            var dif = data[i++];
            if (dif == 0x2F) continue; // idle filler
            if (dif is 0x0F or 0x1F) { more = dif == 0x1F; break; } // manufacturer-specific data until the end
            var coding = dif & 0x0F;
            var function = ((dif >> 4) & 0x03) switch { 0 => "Instantaneous", 1 => "Maximum", 2 => "Minimum", _ => "Value during error" };
            long storage = (dif >> 6) & 0x01; int tariff = 0, subUnit = 0;
            var ext = dif; var difeCount = 0;
            while ((ext & 0x80) != 0)
            {
                if (i >= data.Length) throw new MBusProtocolException("M-Bus record ends inside its DIFE.");
                ext = data[i++];
                storage |= (long)(ext & 0x0F) << (1 + 4 * difeCount);
                tariff |= ((ext >> 4) & 0x03) << (2 * difeCount);
                subUnit |= ((ext >> 6) & 0x01) << difeCount;
                if (++difeCount > 10) throw new MBusProtocolException("Too many M-Bus DIFE bytes.");
            }
            if (i >= data.Length) throw new MBusProtocolException("M-Bus record ends before its VIF.");
            var vif = data[i++];
            var vifes = new List<byte>();
            string? plainUnit = null;
            var vifExt = vif;
            while ((vifExt & 0x80) != 0)
            {
                if (i >= data.Length) throw new MBusProtocolException("M-Bus record ends inside its VIFE.");
                vifExt = data[i++]; vifes.Add(vifExt);
                if (vifes.Count > 10) throw new MBusProtocolException("Too many M-Bus VIFE bytes.");
            }
            if ((vif & 0x7F) == 0x7C) // plain-text unit follows the VIF/VIFE bytes (stored reversed)
            {
                if (i >= data.Length) throw new MBusProtocolException("M-Bus plain-text unit is incomplete.");
                var len = data[i++];
                if (i + len > data.Length) throw new MBusProtocolException("M-Bus plain-text unit is incomplete.");
                var chars = data.Slice(i, len).ToArray(); Array.Reverse(chars); plainUnit = Encoding.ASCII.GetString(chars); i += len;
            }
            var size = coding switch { 0 or 8 => 0, 1 => 1, 2 => 2, 3 => 3, 4 => 4, 5 => 4, 6 => 6, 7 => 8, 9 => 1, 0xA => 2, 0xB => 3, 0xC => 4, 0xE => 6, 0xD => -1, _ => throw new MBusProtocolException($"Unsupported M-Bus data field 0x{coding:X}.") };
            if (size == -1)
            {
                if (i >= data.Length) throw new MBusProtocolException("M-Bus variable-length record is incomplete.");
                var lvar = data[i++];
                size = lvar <= 0xBF ? lvar : lvar <= 0xCF ? lvar - 0xC0 : lvar <= 0xDF ? lvar - 0xD0 : lvar <= 0xEF ? lvar - 0xE0 : throw new MBusProtocolException($"Unsupported M-Bus variable length 0x{lvar:X2}."); // text, BCD, binary
            }
            if (i + size > data.Length) throw new MBusProtocolException("M-Bus record value is incomplete.");
            var raw = data.Slice(i, size); i += size;
            var (quantity, description, unit, factor) = DescribeVif(vif, vifes, plainUnit);
            decimal? value = coding is 0xD or 0 or 8 || factor is null ? null : RawValue(coding, raw);
            if (value is not null && factor is not null) value *= factor.Value;
            records.Add(new(records.Count + 1, quantity, description, value, unit, (int)Math.Min(storage, int.MaxValue), tariff, subUnit, function, Convert.ToHexString(data[start..i])));
        }
        return (records, more);
    }

    static decimal? RawValue(int coding, ReadOnlySpan<byte> raw) => coding switch
    {
        1 => (sbyte)raw[0],
        2 => BinaryPrimitives.ReadInt16LittleEndian(raw),
        3 => SignExtend(raw[0] | raw[1] << 8 | raw[2] << 16, 24),
        4 => BinaryPrimitives.ReadInt32LittleEndian(raw),
        5 => float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(raw)) ? (decimal)BinaryPrimitives.ReadSingleLittleEndian(raw) : null,
        6 => SignExtend((long)BinaryPrimitives.ReadUInt32LittleEndian(raw) | (long)BinaryPrimitives.ReadUInt16LittleEndian(raw[4..]) << 32, 48),
        7 => BinaryPrimitives.ReadInt64LittleEndian(raw),
        9 or 0xA or 0xB or 0xC or 0xE => Bcd(raw),
        _ => null
    };

    static long SignExtend(long value, int bits) { var shift = 64 - bits; return value << shift >> shift; }

    /// <summary>Decodes little-endian BCD. A leading 0xF nibble marks a negative value.</summary>
    static long Bcd(ReadOnlySpan<byte> raw)
    {
        long value = 0; var negative = false;
        for (var n = raw.Length - 1; n >= 0; n--)
        {
            int high = raw[n] >> 4, low = raw[n] & 0x0F;
            if (n == raw.Length - 1 && high == 0xF) { negative = true; high = 0; }
            if (high > 9 || low > 9) throw new MBusProtocolException($"Invalid BCD value {Convert.ToHexString(raw)} (the meter may be reporting an error).");
            value = value * 100 + high * 10 + low;
        }
        return negative ? -value : value;
    }

    static decimal Pow10(int exponent) { var r = 1m; for (var n = 0; n < Math.Abs(exponent); n++) r *= 10; return exponent >= 0 ? r : 1 / r; }

    /// <summary>Unit, quantity and the factor that converts the raw integer to kWh, m³ and so on.</summary>
    static (MBusQuantity, string, string, decimal?) DescribeVif(byte vif, List<byte> vifes, string? plainUnit)
    {
        MBusQuantity q; string d, u; decimal? f;
        var code = vif & 0x7F;
        var tail = vifes;
        if (vif == 0xFB && vifes.Count > 0)
        {
            var e = vifes[0] & 0x7F; tail = vifes.Skip(1).ToList();
            (q, d, u, f) = e switch
            {
                <= 0x01 => (MBusQuantity.Energy, "Energy", "kWh", Pow10((e & 1) - 1 + 3)),                         // MWh 10^(n-1)
                0x08 or 0x09 => (MBusQuantity.Energy, "Energy", "kWh", Pow10((e & 1) - 1) * 1000m / 3.6m),         // GJ 10^(n-1); 1 GJ = 277.78 kWh
                0x10 or 0x11 => (MBusQuantity.Volume, "Volume", "m³", Pow10((e & 1) + 2)),                          // m³ 10^(n+2)
                0x18 or 0x19 => (MBusQuantity.Other, "Mass", "t", Pow10((e & 1) + 2)),
                0x28 or 0x29 => (MBusQuantity.Power, "Power", "kW", Pow10((e & 1) - 1 + 3)),                        // MW 10^(n-1)
                0x30 or 0x31 => (MBusQuantity.Power, "Power", "kW", Pow10((e & 1) - 1) * 1000m / 3.6m),         // GJ/h; 1 GJ/h = 277.78 kW
                _ => (MBusQuantity.Other, $"Extension FB {e:X2}", "", 1m)
            };
        }
        else if (vif == 0xFD && vifes.Count > 0)
        {
            var e = vifes[0] & 0x7F; tail = vifes.Skip(1).ToList();
            (q, d, u, f) = e switch
            {
                0x08 => (MBusQuantity.Identification, "Access number", "", 1m),
                0x0C => (MBusQuantity.Identification, "Model / version", "", 1m),
                0x0E => (MBusQuantity.Identification, "Firmware version", "", 1m),
                0x11 => (MBusQuantity.Identification, "Customer", "", 1m),
                0x17 => (MBusQuantity.Identification, "Error flags", "", 1m),
                >= 0x48 and <= 0x4F => (MBusQuantity.Other, "Voltage", "V", Pow10((e & 0x0F) - 9)),
                >= 0x50 and <= 0x57 => (MBusQuantity.Other, "Current", "A", Pow10((e & 0x0F) - 12)),
                _ => (MBusQuantity.Other, $"Extension FD {e:X2}", "", 1m)
            };
        }
        else if (code == 0x7C) (q, d, u, f) = (MBusQuantity.Other, plainUnit ?? "Plain text unit", plainUnit ?? "", 1m);
        else if (code == 0x7F) (q, d, u, f) = (MBusQuantity.Other, "Manufacturer specific", "", 1m);
        else (q, d, u, f) = code switch
        {
            <= 0x07 => (MBusQuantity.Energy, "Energy", "kWh", Pow10((code & 7) - 3 - 3)),                // Wh 10^(n-3)
            <= 0x0F => (MBusQuantity.Energy, "Energy", "kWh", Pow10(code & 7) / 3_600_000m),             // J 10^n
            <= 0x17 => (MBusQuantity.Volume, "Volume", "m³", Pow10((code & 7) - 6)),
            <= 0x1F => (MBusQuantity.Other, "Mass", "kg", Pow10((code & 7) - 3)),
            <= 0x27 => (MBusQuantity.Other, (code & 4) == 0 ? "On time" : "Operating time", TimeUnit(code), 1m),
            <= 0x2F => (MBusQuantity.Power, "Power", "kW", Pow10((code & 7) - 3 - 3)),                   // W 10^(n-3)
            <= 0x37 => (MBusQuantity.Power, "Power", "kW", Pow10(code & 7) / 3_600_000m),                // J/h
            <= 0x3F => (MBusQuantity.VolumeFlow, "Volume flow", "m³/h", Pow10((code & 7) - 6)),
            <= 0x47 => (MBusQuantity.VolumeFlow, "Volume flow", "m³/h", Pow10((code & 7) - 7) * 60),
            <= 0x4F => (MBusQuantity.VolumeFlow, "Volume flow", "m³/h", Pow10((code & 7) - 9) * 3600),
            <= 0x57 => (MBusQuantity.Other, "Mass flow", "kg/h", Pow10((code & 7) - 3)),
            <= 0x5B => (MBusQuantity.Temperature, "Flow temperature", "°C", Pow10((code & 3) - 3)),
            <= 0x5F => (MBusQuantity.Temperature, "Return temperature", "°C", Pow10((code & 3) - 3)),
            <= 0x63 => (MBusQuantity.Temperature, "Temperature difference", "K", Pow10((code & 3) - 3)),
            <= 0x67 => (MBusQuantity.Temperature, "External temperature", "°C", Pow10((code & 3) - 3)),
            <= 0x6B => (MBusQuantity.Other, "Pressure", "bar", Pow10((code & 3) - 3)),
            0x6C or 0x6D => (MBusQuantity.DateTime, code == 0x6C ? "Date" : "Date and time", "", (decimal?)null),
            0x6E => (MBusQuantity.Other, "Heat cost allocation", "units", 1m),
            <= 0x73 => (MBusQuantity.Other, "Averaging duration", TimeUnit(code), 1m),
            <= 0x77 => (MBusQuantity.Other, "Actuality duration", TimeUnit(code), 1m),
            0x78 => (MBusQuantity.Identification, "Fabrication number", "", 1m),
            0x79 => (MBusQuantity.Identification, "Enhanced identification", "", 1m),
            0x7A => (MBusQuantity.Identification, "Bus address", "", 1m),
            _ => (MBusQuantity.Other, $"VIF {code:X2}", "", 1m)
        };
        foreach (var e in tail.Select(x => x & 0x7F))
        {
            if (e is >= 0x70 and <= 0x77 && f is not null) f *= Pow10((e & 7) - 6); // multiplicative correction
            else if (e == 0x7D && f is not null) f *= 1000;
            else if (e is >= 0x78 and <= 0x7B && f is not null) { } // additive corrections are not used for totals
        }
        return (q, d, u, f);
    }

    static string TimeUnit(int code) => (code & 3) switch { 0 => "s", 1 => "min", 2 => "h", _ => "days" };

    static string Manufacturer(ushort code) => new([(char)(((code >> 10) & 0x1F) + 64), (char)(((code >> 5) & 0x1F) + 64), (char)((code & 0x1F) + 64)]);

    static string MediumName(byte medium) => medium switch
    {
        0x00 => "Other", 0x01 => "Oil", 0x02 => "Electricity", 0x03 => "Gas", 0x04 => "Heat (outlet)", 0x05 => "Steam", 0x06 => "Hot water",
        0x07 => "Water", 0x08 => "Heat cost allocator", 0x09 => "Compressed air", 0x0A => "Cooling (outlet)", 0x0B => "Cooling (inlet)",
        0x0C => "Heat (inlet)", 0x0D => "Heat / cooling", 0x0E => "Bus / system", 0x15 => "Hot water", 0x16 => "Cold water", 0x17 => "Dual water",
        0x18 => "Pressure", 0x19 => "A/D converter", _ => $"Medium 0x{medium:X2}"
    };

    /// <summary>
    /// The record that holds the billing total. <paramref name="recordNumber"/> 0 picks the first current
    /// total of the wanted quantity (energy for electricity and heat, volume for water).
    /// </summary>
    public static MBusRecord SelectBillingRecord(MBusTelegram telegram, MBusQuantity wanted, int recordNumber)
    {
        if (recordNumber > 0)
        {
            var chosen = telegram.Records.FirstOrDefault(x => x.Index == recordNumber)
                ?? throw new MBusProtocolException($"The meter answered with {telegram.Records.Count} records; record {recordNumber} does not exist.");
            if (chosen.Value is null) throw new MBusProtocolException($"Record {recordNumber} ({chosen.Description}) has no numeric value.");
            return chosen;
        }
        return telegram.Records.FirstOrDefault(x => x.Quantity == wanted && x.IsCurrentTotal)
            ?? throw new MBusProtocolException($"The meter's answer has no current {(wanted == MBusQuantity.Volume ? "volume (m³)" : "energy (kWh)")} total. Use Read meter on the meter page and pick the record to bill.");
    }
}
