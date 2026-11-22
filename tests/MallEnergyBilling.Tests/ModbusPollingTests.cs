using System.Reflection;
using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MallEnergyBilling.Tests;

public sealed class ModbusPollingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public async Task DisconnectAndDecreasingRepliesPreserveCountersAndOtherMeterMapping(ushort suspectValue)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var provider = new ServiceCollection()
            .AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connection)).BuildServiceProvider();
        var acceptedAt = DateTimeOffset.UtcNow.AddHours(-1);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();
            var controller = new Controller { Name = "RTU", CommunicationType = "ModbusRtu" };
            var shop = new Shop { Name = "Test", ShopNumber = "T1" };
            foreach (var (address, value) in new[] { (4196, 100m), (4198, 200m) })
                db.Meters.Add(new Meter { Name = $"Meter {address}", SerialNumber = $"TEST-{address}",
                    Controller = controller, Shop = shop, StartingRegister = address,
                    DataType = RegisterDataType.UInt16, ScalingFactor = 1, Active = true,
                    LastReading = value, LastReadingAt = acceptedAt, CommunicationStatus = "Connected" });
            await db.SaveChangesAsync();
        }
        var transport = new FakeModbus();
        var poller = new MeterPollingService(provider.GetRequiredService<IServiceScopeFactory>(), transport,
            null!, null!, new DatabaseMaintenanceService(), NullLogger<MeterPollingService>.Instance);
        async Task Poll() => await (Task)typeof(MeterPollingService)
            .GetMethod("PollController", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(poller, new object[] { 1, CancellationToken.None })!;

        transport.Fail = true;
        await Poll();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var meters = await db.Meters.OrderBy(m => m.StartingRegister).ToArrayAsync();
            Assert.Equal(new[] { 100m, 200m }, meters.Select(m => m.LastReading));
            Assert.All(meters, m => { Assert.Equal(acceptedAt, m.LastReadingAt); Assert.Equal("Failed", m.CommunicationStatus); });
            Assert.Empty(await db.MeterReadings.ToArrayAsync());
            Assert.Equal(new[] { 4196 }, transport.Requested);
        }
        transport.Fail = false;
        transport.Values = new() { [4196] = suspectValue, [4198] = 201 };
        await Poll();
        await Poll(); // repeated bad replies must not become accepted history
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var meters = await db.Meters.OrderBy(m => m.StartingRegister).ToArrayAsync();
            Assert.Equal(100m, meters[0].LastReading);
            Assert.Equal(acceptedAt, meters[0].LastReadingAt);
            Assert.Equal("Counter Review", meters[0].CommunicationStatus);
            Assert.Equal(201m, meters[1].LastReading);
            Assert.Equal("Connected", meters[1].CommunicationStatus);
            Assert.Equal(new[] { 4196, 4198 }, meters.Select(m => m.StartingRegister));
            var review = Assert.Single(await db.MeterReadings.Where(r => r.RequiresReview).ToArrayAsync());
            Assert.Equal(meters[0].Id, review.MeterId);
            Assert.Equal((decimal)suspectValue, review.AccumulatedKwh);
            Assert.DoesNotContain(await db.MeterReadings.ToArrayAsync(), r => r.MeterId == meters[0].Id && !r.RequiresReview);
        }
        transport.Values[4196] = 101;
        await Poll();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var meter = await db.Meters.SingleAsync(m => m.StartingRegister == 4196);
            Assert.Equal(101m, meter.LastReading);
            Assert.Equal("Connected", meter.CommunicationStatus);
            Assert.True(meter.LastReadingAt > acceptedAt);
        }
    }

    [Fact]
    public async Task OffsetMakesTheReadingMatchTheMeterDisplayAndAChangeIsNotBilled()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var provider = new ServiceCollection()
            .AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connection)).BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Meters.Add(new Meter { Name = "PLC meter", SerialNumber = "OFF-1", Controller = new Controller { Name = "PLC", CommunicationType = "ModbusRtu" },
                Shop = new Shop { Name = "Test", ShopNumber = "T1" }, StartingRegister = 10, DataType = RegisterDataType.UInt16,
                ScalingFactor = 0.1m, Offset = 15234.5m, Active = true, CommunicationStatus = "Awaiting poll" });
            await db.SaveChangesAsync();
        }
        var transport = new FakeModbus { Values = new() { [10] = 250 } }; // the PLC has counted 25.0 kWh since it started
        var poller = new MeterPollingService(provider.GetRequiredService<IServiceScopeFactory>(), transport,
            null!, null!, new DatabaseMaintenanceService(), NullLogger<MeterPollingService>.Instance);
        async Task Poll() => await (Task)typeof(MeterPollingService)
            .GetMethod("PollController", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(poller, new object[] { 1, CancellationToken.None })!;
        await Poll();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var meter = await db.Meters.SingleAsync();
            Assert.Equal(15259.5m, meter.LastReading); // same as the meter display
            var reading = await db.MeterReadings.SingleAsync();
            Assert.Equal(15259.5m, reading.AccumulatedKwh);
            Assert.Equal(250UL, reading.RawValue); // the device value is kept unchanged
            // The administrator corrects the offset; the edit page marks the next value as a new starting point.
            meter.Offset = 15300m; meter.RebaseOnNextReading = true;
            await db.SaveChangesAsync();
        }
        transport.Values[10] = 260;
        await Poll();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var readings = await db.MeterReadings.OrderBy(r => r.Id).ToArrayAsync();
            Assert.Equal(15326m, readings[^1].AccumulatedKwh);
            Assert.Equal(BillingRunService.BaselineQuality, readings[^1].Quality); // the +40.5 offset change is a new start, not consumption
            Assert.Equal(260UL, readings[^1].RawValue);
        }
    }

    private sealed class FakeModbus : IModbusService
    {
        public bool Fail { get; set; }
        public Dictionary<int, ushort> Values { get; set; } = new();
        public List<int> Requested { get; } = new();
        public Task<ModbusReadResult> ReadHoldingRegistersAsync(Controller c, int address, ushort count, CancellationToken ct = default)
        {
            Requested.Add(address);
            if (Fail) throw new TimeoutException("Disconnected RTU controller");
            return Task.FromResult(new ModbusReadResult(new[] { Values[address] }, "request", "response"));
        }
    }
}
