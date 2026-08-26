using MallEnergyBilling.Web.Data;using MallEnergyBilling.Web.Models;using Microsoft.EntityFrameworkCore;
namespace MallEnergyBilling.Web.Services;
public sealed class MeterPollingService(IServiceScopeFactory scopes,IModbusRtuService modbus,DatabaseMaintenanceService maintenance,ILogger<MeterPollingService> log):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken ct)
 {
  while(!ct.IsCancellationRequested)
  {
   try
   {
    await maintenance.Gate.WaitAsync(ct);
    try
    {
    using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var meters=await db.Meters.Include(x=>x.Controller).Where(x=>x.Active&&x.Controller!.Enabled).ToListAsync(ct);
    foreach(var m in meters)
    {
     if(m.Controller!.CommunicationType=="ModbusRtu")await UpdateRtu(db,m,ct);
     await SaveHistoryIfDue(db,m,ct);
    }
    await db.SaveChangesAsync(ct);
    }
    finally{maintenance.Gate.Release();}
   }
   catch(OperationCanceledException)when(ct.IsCancellationRequested){}
   catch(Exception ex){log.LogError(ex,"Polling cycle failed; retrying");}
   await Task.Delay(TimeSpan.FromSeconds(5),ct);
  }
 }
 async Task UpdateRtu(ApplicationDbContext db,Meter m,CancellationToken ct)
 {
  try
  {
   var count=ModbusValueConverter.RegisterCount(m.DataType);var result=await modbus.ReadHoldingRegistersAsync(m.Controller!,m.StartingRegister,count,ct);var value=ModbusValueConverter.ConvertValue(result.Registers,m.DataType,m.WordOrder,m.ScalingFactor);
   if(value<m.LastReading&&m.LastReadingAt is not null)db.MeterReadings.Add(new(){MeterId=m.Id,RawValue=(ulong)Math.Max(0,value/m.ScalingFactor),AccumulatedKwh=value,Timestamp=DateTimeOffset.UtcNow,Source=ReadingSource.Automatic,Quality="Review: lower than previous",RequiresReview=true});
   m.LastReading=value;m.LastReadingAt=DateTimeOffset.UtcNow;m.CommunicationStatus="Connected";m.Controller!.LastSuccess=DateTimeOffset.UtcNow;m.Controller.Condition="Connected";m.Controller.Notes=$"Last RTU request {result.RequestHex}; response {result.ResponseHex}";
  }
  catch(Exception ex){m.CommunicationStatus="Failed";m.Controller!.Condition="Failed";m.Controller.Notes=ex.Message;log.LogWarning(ex,"RTU read failed for meter {Meter} on {Port}",m.Name,m.Controller.ComPort);}
 }
 static async Task SaveHistoryIfDue(ApplicationDbContext db,Meter m,CancellationToken ct){if(m.LastReadingAt is null||m.CommunicationStatus!="Connected")return;var last=await db.MeterReadings.Where(x=>x.MeterId==m.Id).OrderByDescending(x=>x.Id).FirstOrDefaultAsync(ct);if(last is null||DateTimeOffset.UtcNow-last.Timestamp>=TimeSpan.FromMinutes(15))db.MeterReadings.Add(new(){MeterId=m.Id,RawValue=(ulong)Math.Max(0,m.LastReading/m.ScalingFactor),AccumulatedKwh=m.LastReading,Timestamp=DateTimeOffset.UtcNow,Source=ReadingSource.Automatic,Quality="Good"});}
}
