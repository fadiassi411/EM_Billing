using System.Net;
using System.Net.Sockets;
using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MallEnergyBilling.Tests;

public sealed class MBusProtocolTests
{
    // Long header: ID 12345678, manufacturer KAM, version 1, medium heat, access 0x10, status 0, signature 0.
    static readonly byte[] Header = [0x78, 0x56, 0x34, 0x12, 0x2D, 0x2C, 0x01, 0x04, 0x10, 0x00, 0x00, 0x00];

    internal static byte[] Response(byte address, params byte[] records) => MBusProtocol.LongFrame(0x08, address, 0x72, [.. Header, .. records]);

    internal static readonly byte[] HeatMeterRecords =
    [
        0x04, 0x06, 0x39, 0x30, 0x00, 0x00,        // #1 energy 12345 kWh
        0x04, 0x14, 0x40, 0xE2, 0x01, 0x00,        // #2 volume 123456 x 0.01 m³
        0x44, 0x06, 0x10, 0x27, 0x00, 0x00,        // #3 energy, storage 1 (previous due date) 10000 kWh
        0x0C, 0x13, 0x78, 0x56, 0x34, 0x12,        // #4 volume BCD 12345678 x 0.001 m³
        0x02, 0x5A, 0x8F, 0x02,                    // #5 flow temperature 65.5 °C
        0x84, 0x10, 0x06, 0xF4, 0x01, 0x00, 0x00,  // #6 energy tariff 1, 500 kWh
        0x0F, 0x01, 0x02                           // manufacturer data
    ];

    [Fact] public void BuildsShortFrames()
    {
        Assert.Equal(new byte[] { 0x10, 0x40, 0x05, 0x45, 0x16 }, MBusProtocol.SndNke(5));
        Assert.Equal(new byte[] { 0x10, 0x7B, 0x05, 0x80, 0x16 }, MBusProtocol.ReqUd2(5));
    }

    [Fact] public void BuildsSecondaryAddressSelection()
    {
        Assert.Equal(Convert.FromHexString("680B0B6853FD5278563412FFFFFFFFB216"), MBusProtocol.SelectSecondary("1234 5678"));
        Assert.Null(MBusProtocol.NormalizeSecondaryAddress("1234567"));
        Assert.Null(MBusProtocol.NormalizeSecondaryAddress("1234567A"));
    }

    [Fact] public void DecodesHeaderAndRecords()
    {
        var t = MBusProtocol.ParseResponse(Response(7, HeatMeterRecords));
        Assert.Equal(7, t.Address);
        Assert.Equal("12345678", t.SecondaryAddress);
        Assert.Equal("KAM", t.Manufacturer);
        Assert.Equal("Heat (outlet)", t.Medium);
        Assert.Equal(6, t.Records.Count);
        Assert.Equal(12345m, t.Records[0].Value);
        Assert.Equal(MBusQuantity.Energy, t.Records[0].Quantity);
        Assert.Equal(1234.56m, t.Records[1].Value);
        Assert.Equal("m³", t.Records[1].Unit);
        Assert.Equal(1, t.Records[2].StorageNumber);
        Assert.Equal(12345.678m, t.Records[3].Value);
        Assert.Equal(65.5m, t.Records[4].Value);
        Assert.Equal(MBusQuantity.Temperature, t.Records[4].Quantity);
        Assert.Equal(1, t.Records[5].Tariff);
        Assert.False(t.MoreRecordsFollow);
    }

    [Fact] public void PicksTheCurrentTotalForBilling()
    {
        var t = MBusProtocol.ParseResponse(Response(7, HeatMeterRecords));
        Assert.Equal(1, MBusProtocol.SelectBillingRecord(t, MBusQuantity.Energy, 0).Index);  // not the stored or tariff values
        Assert.Equal(2, MBusProtocol.SelectBillingRecord(t, MBusQuantity.Volume, 0).Index);
        Assert.Equal(12345.678m, MBusProtocol.SelectBillingRecord(t, MBusQuantity.Volume, 4).Value);
        Assert.Throws<MBusProtocolException>(() => MBusProtocol.SelectBillingRecord(t, MBusQuantity.Energy, 9));
        var noEnergy = MBusProtocol.ParseResponse(Response(7, 0x02, 0x5A, 0x8F, 0x02));
        Assert.Throws<MBusProtocolException>(() => MBusProtocol.SelectBillingRecord(noEnergy, MBusQuantity.Energy, 0));
    }

    [Fact] public void ConvertsLargeEnergyUnitsToKwh()
    {
        var t = MBusProtocol.ParseResponse(Response(1,
            0x04, 0xFB, 0x00, 0x19, 0x00, 0x00, 0x00,   // 25 x 0.1 MWh
            0x04, 0xFB, 0x09, 0x02, 0x00, 0x00, 0x00,   // 2 GJ
            0x04, 0x03, 0xE8, 0x03, 0x00, 0x00,         // 1000 Wh
            0x04, 0x86, 0x7D, 0x05, 0x00, 0x00, 0x00)); // 5 kWh x 1000 (VIFE)
        Assert.Equal(2500m, t.Records[0].Value);
        Assert.Equal(555.56m, Math.Round(t.Records[1].Value!.Value, 2));
        Assert.Equal(1m, t.Records[2].Value);
        Assert.Equal(5000m, t.Records[3].Value);
    }

    [Fact] public void DecodesWaterMeterWithShortHeaderAndRealValue()
    {
        var value = BitConverter.GetBytes(842.125f);
        var frame = MBusProtocol.LongFrame(0x08, 3, 0x7A, [0x22, 0x00, 0x00, 0x00, 0x05, 0x13, .. value]);
        var t = MBusProtocol.ParseResponse(frame);
        var record = MBusProtocol.SelectBillingRecord(t, MBusQuantity.Volume, 0);
        Assert.Equal(0.842125m, record.Value); // 842.125 litres
    }

    [Fact] public void NegativeBcdAndSignedIntegers()
    {
        var t = MBusProtocol.ParseResponse(Response(1, 0x0A, 0x5E, 0x50, 0xF0, 0x02, 0x60, 0xFE, 0xFF));
        Assert.Equal(-5.0m, t.Records[0].Value);   // return temperature BCD -050 x 0.1
        Assert.Equal(-0.002m, t.Records[1].Value); // temperature difference -2 mK
    }

    [Fact] public void RejectsDamagedFrames()
    {
        var frame = Response(1, HeatMeterRecords);
        frame[^2] ^= 0xFF;
        Assert.Throws<MBusProtocolException>(() => MBusProtocol.ParseResponse(frame));
        Assert.Throws<MBusProtocolException>(() => MBusProtocol.ParseResponse(Response(1, 0x04, 0x06, 0x39)));
        Assert.Throws<MBusProtocolException>(() => MBusProtocol.ParseResponse(Response(1, 0x0C, 0x06, 0xEE, 0xEE, 0xEE, 0xEE))); // not BCD
    }

    [Fact] public void ReportsMoreRecordsInNextTelegram()
    {
        Assert.True(MBusProtocol.ParseResponse(Response(1, 0x04, 0x06, 0x01, 0x00, 0x00, 0x00, 0x1F)).MoreRecordsFollow);
    }

    [Fact] public void FindsCompleteFrames()
    {
        var frame = Response(1, HeatMeterRecords);
        Assert.Equal(0, MBusProtocol.CompleteFrameLength(frame.AsSpan(0, 10)));
        Assert.Equal(frame.Length, MBusProtocol.CompleteFrameLength(frame));
        Assert.Equal(1, MBusProtocol.CompleteFrameLength(new byte[] { 0xE5 }));
    }
}

/// <summary>A fake transparent M-Bus/Ethernet gateway with meters at primary addresses and 8-digit IDs.</summary>
internal sealed class FakeMBusGateway : IAsyncDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource stop = new();
    readonly Task loop;
    public int Port { get; }
    public Dictionary<byte, byte[]> MetersByPrimary { get; } = [];
    public Dictionary<string, byte[]> MetersBySecondary { get; } = [];
    public bool SilentReset { get; set; }

    public FakeMBusGateway() { listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port; loop = Task.Run(AcceptLoop); }

    async Task AcceptLoop()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop.Token); } catch { return; }
            _ = Task.Run(() => Serve(client));
        }
    }

    async Task Serve(TcpClient client)
    {
        using var _ = client; var stream = client.GetStream(); string? selected = null;
        var buffer = new List<byte>(); var chunk = new byte[256];
        try
        {
            while (true)
            {
                var n = await stream.ReadAsync(chunk, stop.Token); if (n == 0) return;
                buffer.AddRange(chunk.AsSpan(0, n));
                while (buffer.Count > 0 && MBusProtocol.CompleteFrameLength(buffer.ToArray()) is var len and > 0)
                {
                    var f = buffer.GetRange(0, len).ToArray(); buffer.RemoveRange(0, len);
                    byte[]? reply = null;
                    if (f[0] == 0x10 && f[1] == 0x40) { if (f[2] == 0xFD) selected = null; else if (!SilentReset && MetersByPrimary.ContainsKey(f[2])) reply = [0xE5]; }
                    else if (f[0] == 0x10 && (f[1] & 0xDF) == 0x5B)
                        reply = f[2] == 0xFD ? selected is null ? null : MetersBySecondary[selected] : MetersByPrimary.GetValueOrDefault(f[2]);
                    else if (f[0] == 0x68 && f[6] == 0x52)
                    {
                        var id = $"{f[10]:X2}{f[9]:X2}{f[8]:X2}{f[7]:X2}";
                        if (MetersBySecondary.ContainsKey(id)) { selected = id; reply = [0xE5]; }
                    }
                    if (reply is not null) await stream.WriteAsync(reply, stop.Token);
                }
            }
        }
        catch { }
    }

    public async ValueTask DisposeAsync() { stop.Cancel(); listener.Stop(); try { await loop; } catch { } }
}

public sealed class MBusGatewayTests
{
    static Controller Gateway(int port) => new() { CommunicationType = "MBusTcp", IpAddress = "127.0.0.1", TcpPort = port, TimeoutMilliseconds = 600, RetryCount = 0 };

    [Fact] public async Task ReadsMeterByPrimaryAddress()
    {
        await using var gateway = new FakeMBusGateway();
        gateway.MetersByPrimary[7] = MBusProtocolTests.Response(7, MBusProtocolTests.HeatMeterRecords);
        var result = await new MBusService(NullLogger<MBusService>.Instance).ReadAsync(Gateway(gateway.Port), 7, null);
        Assert.Equal(12345m, MBusProtocol.SelectBillingRecord(result.Telegram, MBusQuantity.Energy, 0).Value);
        Assert.Equal("primary address 7", result.AddressText);
    }

    [Fact] public async Task ReadsMeterThatDoesNotAcknowledgeTheReset()
    {
        await using var gateway = new FakeMBusGateway { SilentReset = true };
        gateway.MetersByPrimary[9] = MBusProtocolTests.Response(9, MBusProtocolTests.HeatMeterRecords);
        var result = await new MBusService(NullLogger<MBusService>.Instance).ReadAsync(Gateway(gateway.Port), 9, "");
        Assert.Equal(6, result.Telegram.Records.Count);
    }

    [Fact] public async Task ReadsMeterBySecondaryAddress()
    {
        await using var gateway = new FakeMBusGateway();
        gateway.MetersBySecondary["12345678"] = MBusProtocolTests.Response(0xFD, MBusProtocolTests.HeatMeterRecords);
        var result = await new MBusService(NullLogger<MBusService>.Instance).ReadAsync(Gateway(gateway.Port), 0, "12345678");
        Assert.Equal("12345678", result.Telegram.SecondaryAddress);
        await Assert.ThrowsAsync<MBusNoAnswerException>(() => new MBusService(NullLogger<MBusService>.Instance).ReadAsync(Gateway(gateway.Port), 0, "87654321"));
    }

    [Fact] public async Task MissingMeterIsReportedAsNoAnswer()
    {
        await using var gateway = new FakeMBusGateway();
        var ex = await Assert.ThrowsAsync<MBusNoAnswerException>(() => new MBusService(NullLogger<MBusService>.Instance).ReadAsync(Gateway(gateway.Port), 4, null));
        Assert.Contains("primary address 4", ex.Message);
    }

    [Fact] public async Task UnreachableGatewayIsNotANoAnswer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => new MBusService(NullLogger<MBusService>.Instance).ReadAsync(Gateway(port), 1, null));
        Assert.IsNotType<MBusNoAnswerException>(ex);
    }

    [Fact] public async Task PollingUsesEnergyForElectricityAndVolumeForWater()
    {
        await using var gateway = new FakeMBusGateway();
        gateway.MetersByPrimary[7] = MBusProtocolTests.Response(7, MBusProtocolTests.HeatMeterRecords);
        var service = new MBusService(NullLogger<MBusService>.Instance);
        var electricity = new Meter { Name = "E", StartingRegister = 7, UtilityType = UtilityType.Electricity, ScalingFactor = 1 };
        var (kwh, detail) = await MeterPollingService.ReadMBusAsync(service, Gateway(gateway.Port), electricity, default);
        Assert.Equal(12345m, kwh);
        Assert.Contains("ID 12345678", detail);
        var water = new Meter { Name = "W", StartingRegister = 7, UtilityType = UtilityType.Water, ScalingFactor = 1 };
        Assert.Equal(1234.56m, (await MeterPollingService.ReadMBusAsync(service, Gateway(gateway.Port), water, default)).Value);
        var chosen = new Meter { Name = "W2", StartingRegister = 7, UtilityType = UtilityType.Water, ScalingFactor = 2, MBusRecord = 4 };
        Assert.Equal(24691.356m, (await MeterPollingService.ReadMBusAsync(service, Gateway(gateway.Port), chosen, default)).Value);
        var withOffset = new Meter { Name = "E2", StartingRegister = 7, UtilityType = UtilityType.Electricity, ScalingFactor = 1, Offset = -45m };
        Assert.Equal(12300m, (await MeterPollingService.ReadMBusAsync(service, Gateway(gateway.Port), withOffset, default)).Value);
    }

    [Fact] public async Task SilentMeterDoesNotStopTheOthersOnTheBus()
    {
        await using var gateway = new FakeMBusGateway();
        gateway.MetersByPrimary[7] = MBusProtocolTests.Response(7, MBusProtocolTests.HeatMeterRecords);
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var provider = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddDbContext<MallEnergyBilling.Web.Data.ApplicationDbContext>(o => o.UseSqlite(connection)).BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MallEnergyBilling.Web.Data.ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();
            var controller = Gateway(gateway.Port); controller.Name = "MBUS";
            var shop = new Shop { Name = "Test", ShopNumber = "T1" };
            foreach (var address in new[] { 4, 7 }) // nothing answers at 4
                db.Meters.Add(new Meter { Name = $"Meter {address}", SerialNumber = $"MB-{address}", Controller = controller, Shop = shop, StartingRegister = address, ScalingFactor = 1, Active = true, CommunicationStatus = "Awaiting poll" });
            await db.SaveChangesAsync();
        }
        var poller = new MeterPollingService(provider.GetRequiredService<IServiceScopeFactory>(), null!, null!, new MBusService(NullLogger<MBusService>.Instance), new DatabaseMaintenanceService(), NullLogger<MeterPollingService>.Instance);
        await (Task)typeof(MeterPollingService).GetMethod("PollController", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(poller, [1, CancellationToken.None])!;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MallEnergyBilling.Web.Data.ApplicationDbContext>();
            var meters = await db.Meters.OrderBy(m => m.StartingRegister).ToArrayAsync();
            Assert.Equal("Failed", meters[0].CommunicationStatus);
            Assert.Equal("Connected", meters[1].CommunicationStatus);
            Assert.Equal(12345m, meters[1].LastReading);
            Assert.Single(await db.MeterReadings.ToArrayAsync());
        }
    }
}
