using MallEnergyBilling.Web.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MallEnergyBilling.Tests;

public sealed class BacnetMigrationTests
{
    [Fact] public async Task UpgradePreservesLegacyDataAndRequiresEnergyConfirmation()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20260908164231_AddBacnetIpSupport");
        // Insert old schema rows without referencing properties added by the new model.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Controllers (Id,Name,MdbPanel,CommunicationType,ComPort,BaudRate,Parity,DataBits,StopBits,IpAddress,TcpPort,BacnetLocalIp,BacnetUdpPort,BacnetDeviceInstance,SlaveAddress,PollingIntervalSeconds,TimeoutMilliseconds,RetryCount,Enabled,Condition,Notes)
            VALUES (1,'BACnet device','Panel','BacnetIp','COM1',9600,'Even',8,1,'192.0.2.1',502,'192.0.2.2',47808,0,1,5,1000,2,1,'Connected',''),
                   (2,'Modbus device','Panel','ModbusRtu','COM9',19200,'Odd',8,1,'',502,'',47808,0,7,5,1000,2,1,'Connected','');
            INSERT INTO Shops (Id,ShopNumber,Name,TenantName,ContactPerson,Telephone,Email,Floor,Zone,MdbPanel,Status,BillingAddress,TaxNumber,Notes) VALUES (1,'S1','Shop','','','','','','','',0,'','','');
            INSERT INTO Meters (Id,Name,SerialNumber,Model,ShopId,ControllerId,UtilityType,PowerSource,StartingRegister,BacnetObjectType,BacnetObjectInstance,DataType,WordOrder,ScalingFactor,PulseConstant,CtPrimary,CtSecondary,CtAppliedByPlc,InitialReading,CommissioningDate,Active,LastReading,CommunicationStatus,SimulatedLoadKw,SimulationRunning,SimulateFailure,SimulateFrozen,Notes)
            VALUES (1,'Energy','BAC1','',1,1,0,0,0,'Accumulator',12,0,0,'1',1600,'1','1',1,'0','2026-09-01',1,'123','Connected','0',0,0,0,''),
                   (2,'Modbus','MOD1','',1,2,0,0,4196,'AnalogInput',0,2,1,'0.01',1600,'1','1',1,'0','2026-09-01',1,'456','Connected','0',0,0,0,'');
            INSERT INTO MeterReadings (MeterId,RawValue,AccumulatedKwh,Timestamp,Source,Quality,Reason,UsedForBilling,RequiresReview) VALUES (1,123,'123','2026-09-01 00:00:00+00:00',0,'Good','',0,0);
            """);
        await db.Database.MigrateAsync();
        var controller=await db.Controllers.FindAsync(1);Assert.Equal(0,controller!.BacnetDeviceInstance);
        var point=await db.BacnetPoints.SingleAsync();Assert.Equal("Accumulator",point.ObjectType);Assert.Equal(12,point.ObjectInstance);Assert.False(point.BillingConfirmed);
        Assert.Equal(1m,point.Scale);Assert.Single(await db.MeterReadings.ToListAsync());Assert.Equal(2,await db.Meters.CountAsync());
        var modbus=await db.Controllers.FindAsync(2);Assert.Equal("COM9",modbus!.ComPort);Assert.Equal(7,modbus.SlaveAddress);
        var meter=await db.Meters.FindAsync(2);Assert.Equal(4196,meter!.StartingRegister);Assert.Equal(.01m,meter.ScalingFactor);
        controller.BacnetDeviceInstance=null;await db.SaveChangesAsync();db.ChangeTracker.Clear();Assert.Null((await db.Controllers.FindAsync(1))!.BacnetDeviceInstance);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
