using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Services;

public sealed class InvoiceSchedulerService(IServiceScopeFactory scopes, ILogger<InvoiceSchedulerService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunDueSchedules(stoppingToken); }
            catch (Exception ex) { log.LogError(ex, "Scheduled invoice generation failed."); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    async Task RunDueSchedules(CancellationToken token)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var calculator = scope.ServiceProvider.GetRequiredService<BillingCalculator>();
        var resolver = scope.ServiceProvider.GetRequiredService<TariffResolver>();
        var now = DateTimeOffset.Now;
        var schedules = await db.InvoiceSchedules.Where(x => !x.Completed).ToListAsync(token);
        foreach (var schedule in schedules.Where(x => x.GenerateAt <= now))
        {
            var localRun = schedule.GenerateAt.LocalDateTime;
            var invoiceDate = DateOnly.FromDateTime(localRun);
            var periodEnd = new DateOnly(localRun.Year, localRun.Month, 1).AddDays(-1);
            var periodStart = new DateOnly(periodEnd.Year, periodEnd.Month, 1);
            var outcome = await Generate(db, calculator, resolver, periodStart, periodEnd, invoiceDate, invoiceDate.AddDays(schedule.DueDays), schedule.CreatedBy, token);
            schedule.CompletedAt = DateTimeOffset.Now; schedule.Result = outcome;
            var nextMonth = new DateOnly(localRun.Year, localRun.Month, 1).AddMonths(1);
            var nextLocal = nextMonth.AddDays(schedule.PublicationDay - 1).ToDateTime(new TimeOnly(0, 5));
            schedule.GenerateAt = new DateTimeOffset(nextLocal, TimeZoneInfo.Local.GetUtcOffset(nextLocal));
            db.AuditLogs.Add(new(){Timestamp=DateTimeOffset.UtcNow,UserId=schedule.CreatedBy,Action="Scheduled invoice run completed",EntityType="InvoiceSchedule",EntityId=schedule.Id.ToString(),NewValue=outcome,Reason="Automatic monthly billing",SourceIp="Background service"});
            await db.SaveChangesAsync(token);
        }
    }

    static async Task<string> Generate(ApplicationDbContext db, BillingCalculator calculator, TariffResolver resolver, DateOnly startDate, DateOnly endDate, DateOnly invoiceDate, DateOnly dueDate, string user, CancellationToken token)
    {
        if (await db.BillingPeriods.AnyAsync(x => x.StartDate == startDate && x.EndDate == endDate, token)) return "Skipped: billing period already exists.";
        static DateTimeOffset AtStart(DateOnly date){var value=date.ToDateTime(TimeOnly.MinValue);return new(value,TimeZoneInfo.Local.GetUtcOffset(value));}
        var start=AtStart(startDate); var endExclusive=AtStart(endDate.AddDays(1)); var issued=AtStart(invoiceDate); var due=AtStart(dueDate);
        var meters=await db.Meters.Include(x=>x.Shop).Where(x=>x.Active).OrderBy(x=>x.Id).ToListAsync(token);
        var readings=(await db.MeterReadings.ToListAsync(token)).Where(x=>x.Timestamp<endExclusive).OrderBy(x=>x.Timestamp).ToList();
        var tariffs=await db.Tariffs.ToListAsync(token);
        var priorInvoices=await db.Invoices.Include(x=>x.Payments).Where(x=>x.Status!=InvoiceStatus.Cancelled).ToListAsync(token);
        var items=new List<(Meter meter,MeterReading? opening,MeterReading closing,Tariff tariff,BillingResult result,decimal previous)>();
        var errors=new List<string>();
        foreach(var meter in meters)
        {
            var rows=readings.Where(x=>x.MeterId==meter.Id).ToList(); var opening=rows.LastOrDefault(x=>x.Timestamp<start); var closing=rows.LastOrDefault(x=>x.Timestamp>=start&&x.Timestamp<endExclusive);
            if(closing is null){errors.Add($"{meter.Name}: no reading");continue;}
            Tariff tariff; try{tariff=resolver.Resolve(tariffs,meter.Id,issued);}catch(InvalidOperationException){errors.Add($"{meter.Name}: no tariff");continue;}
            var previous=AccountBalanceService.Outstanding(priorInvoices.Where(x=>x.MeterId==meter.Id));
            try{var result=calculator.Calculate(new(opening?.AccumulatedKwh??meter.InitialReading,closing.AccumulatedKwh,tariff.PricePerKwh,0,0,0,0,previous));items.Add((meter,opening,closing,tariff,result,previous));}
            catch(InvalidOperationException ex){errors.Add($"{meter.Name}: {ex.Message}");}
        }
        if(errors.Count>0) return "No invoices created: " + string.Join("; ", errors);
        var period=new BillingPeriod{Number=$"BP-{startDate:yyyyMM}",StartDate=startDate,EndDate=endDate,Status=BillingStatus.Finalized,CreatedAt=DateTimeOffset.UtcNow,FinalizedAt=DateTimeOffset.UtcNow,ResponsibleUser=user};
        db.BillingPeriods.Add(period); await db.SaveChangesAsync(token);
        var next=(await db.Invoices.Select(x=>(int?)x.Id).MaxAsync(token)??0)+1;
        foreach(var item in items)
        {
            item.closing.UsedForBilling=true;if(item.opening is not null)item.opening.UsedForBilling=true;
            db.Invoices.Add(new(){InvoiceNumber=$"INV-{next++:000000}",BillingPeriodId=period.Id,MeterId=item.meter.Id,ShopId=item.meter.ShopId,InvoiceDate=issued,DueDate=due,OpeningReading=item.opening?.AccumulatedKwh??item.meter.InitialReading,ClosingReading=item.closing.AccumulatedKwh,ConsumptionKwh=item.result.Consumption,TariffPerKwh=item.tariff.PricePerKwh,EnergyCharge=item.result.EnergyCharge,PreviousBalance=item.previous,Total=item.result.Total,Currency=item.tariff.Currency,Status=InvoiceStatus.Finalized,Locked=true,SnapshotJson=System.Text.Json.JsonSerializer.Serialize(new{PeriodStart=startDate,PeriodEnd=endDate,Tariff=item.tariff.PricePerKwh,item.tariff.Currency})});
        }
        await db.SaveChangesAsync(token); return $"{items.Count} invoice(s) published for {startDate:dd MMM yyyy}–{endDate:dd MMM yyyy}.";
    }
}
