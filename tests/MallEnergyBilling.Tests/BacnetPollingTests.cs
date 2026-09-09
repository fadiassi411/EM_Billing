using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MallEnergyBilling.Tests;

public sealed class BacnetPollingTests
{
    [Fact]
    public async Task PollingUsesConfirmedEnergyPreservesHistoryOnFailureAndRecovers()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        var services=new ServiceCollection().AddDbContext<ApplicationDbContext>(o=>o.UseSqlite(connection)).BuildServiceProvider();
        await using var provider=services;
        using(var scope=provider.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();await db.Database.EnsureCreatedAsync();
            var c=new Controller{Name="Device",CommunicationType="BacnetIp",BacnetLocalIp="127.0.0.1",IpAddress="127.0.0.2",BacnetDeviceInstance=0};
            var m=new Meter{Name="Meter",SerialNumber="TEST-1",Controller=c,Shop=new(){Name="Test",ShopNumber="T1"},Active=true};
            m.BacnetPoints.Add(new(){Name="Energy",ObjectType="Accumulator",ObjectInstance=1,Measurement=MeasurementType.TotalImportEnergy,Unit="kWh",BillingConfirmed=true});
            m.BacnetPoints.Add(new(){Name="Voltage",ObjectInstance=2,Measurement=MeasurementType.VoltageL1,Unit="V"});
            db.Meters.Add(m);await db.SaveChangesAsync();
        }
        var fake=new BacnetTests.FakeTransport();var service=new BacnetIpService(fake,NullLogger<BacnetIpService>.Instance);
        var poller=new BacnetPollingService(provider.GetRequiredService<IServiceScopeFactory>(),service,new DatabaseMaintenanceService(),NullLogger<BacnetPollingService>.Instance);
        await poller.PollAsync(1,default);
        DateTimeOffset? successfulAt;
        using(var scope=provider.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var meter=await db.Meters.SingleAsync();
            Assert.Equal(10m,meter.LastReading);Assert.Equal("Online",meter.CommunicationStatus);successfulAt=meter.LastReadingAt;
            Assert.Single(await db.MeterReadings.ToListAsync());
        }
        fake.FailRpm=true;fake.FailedInstance=1;await poller.PollAsync(1,default);
        using(var scope=provider.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var meter=await db.Meters.SingleAsync();
            Assert.Equal(successfulAt,meter.LastReadingAt);Assert.Equal("Stale",meter.CommunicationStatus);Assert.Single(await db.MeterReadings.ToListAsync());
            Assert.Equal("Good",(await db.BacnetPoints.SingleAsync(x=>x.ObjectInstance==2)).Quality);
        }
        fake.FailedInstance=-1;await poller.PollAsync(1,default);
        using(var scope=provider.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var meter=await db.Meters.SingleAsync();Assert.Equal("Online",meter.CommunicationStatus);
            meter.LastReading=20;await db.SaveChangesAsync();
        }
        await poller.PollAsync(1,default);
        using(var scope=provider.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var meter=await db.Meters.SingleAsync();
            Assert.True(meter.BacnetCounterReview);Assert.Equal(20m,meter.LastReading);Assert.Equal("Counter Review",meter.CommunicationStatus);
            Assert.Single(await db.MeterReadings.Where(x=>x.RequiresReview).ToListAsync());
        }
        await poller.PollAsync(1,default);
        using(var scope=provider.CreateScope())Assert.Equal(2,await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().MeterReadings.CountAsync());
    }
}
