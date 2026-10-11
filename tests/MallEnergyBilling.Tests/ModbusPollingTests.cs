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
            null!, new DatabaseMaintenanceService(), NullLogger<MeterPollingService>.Instance);
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
