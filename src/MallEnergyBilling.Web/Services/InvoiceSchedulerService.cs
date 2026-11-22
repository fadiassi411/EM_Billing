using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Services;

/// <summary>
/// Publishes the invoices of each completed billing cycle. Every meter that is ready is billed on time;
/// a meter with a problem (no tariff, no reading, counter to review) waits on its own and is retried
/// every few minutes, so one meter never holds back the other shops. If the run itself cannot start
/// (for example overlapping dates), it is retried every hour and the month is never skipped silently.
/// </summary>
public sealed class InvoiceSchedulerService(IServiceScopeFactory scopes, TimeProvider time, ILogger<InvoiceSchedulerService> log) : BackgroundService
{
    public static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);
    public static readonly TimeSpan WaitingMeterInterval = TimeSpan.FromMinutes(5);
    DateTimeOffset nextWaitingCheck = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await RunDueSchedulesAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { log.LogError(ex, "Scheduled invoice generation failed."); }
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    /// <summary>Runs every schedule that is due, then retries meters that are waiting. Public so tests can drive it with a fake clock.</summary>
    public async Task RunDueSchedulesAsync(CancellationToken token = default)
    {
        await RunSchedulesAsync(token);
        var now = time.GetUtcNow();
        // A check "in the future" means the PC clock was moved back: check now.
        if (now < nextWaitingCheck && nextWaitingCheck - now <= WaitingMeterInterval) return;
        nextWaitingCheck = now + WaitingMeterInterval;
        using var scope = scopes.CreateScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<BillingRunService>().BillWaitingMetersAsync(ct: token);
        if (outcome.Created) log.LogInformation("Waiting meters: {Result}", outcome.Message);
    }

    async Task RunSchedulesAsync(CancellationToken token)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var billing = scope.ServiceProvider.GetRequiredService<BillingRunService>();
        var schedules = await db.InvoiceSchedules.Where(x => !x.Completed).ToListAsync(token);
        // Catch up one cycle at a time (e.g. after the server was switched off for two months).
        for (var guard = 0; guard < 24; guard++)
        {
            var now = time.GetUtcNow();
            // A last attempt "in the future" means the PC clock was moved forward and back again: retry now.
            var due = schedules.Where(x => x.GenerateAt <= now && (x.LastAttemptAt is null || x.LastAttemptAt > now || now - x.LastAttemptAt >= RetryInterval)).ToList();
            if (due.Count == 0) return;
            var progressed = false;
            foreach (var schedule in due) progressed |= await RunAsync(db, billing, schedule, now, token);
            if (!progressed) return;
        }
    }

    async Task<bool> RunAsync(ApplicationDbContext db, BillingRunService billing, InvoiceSchedule schedule, DateTimeOffset now, CancellationToken token)
    {
        var runDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(schedule.GenerateAt, TimeZoneInfo.Local).DateTime);
        var cycleStart = BillingCycle.CycleStartForRun(runDate, schedule.PublishDelayDays);
        var (periodStart, periodEnd) = BillingCycle.PeriodEndingBefore(cycleStart);
        var outcome = await billing.GenerateAsync(new BillingRunRequest(periodStart, periodEnd, runDate, runDate.AddDays(schedule.DueDays),
            Publish: true, User: schedule.CreatedBy, Reason: "Automatic monthly billing"), token);
        db.ChangeTracker.Clear();
        db.Attach(schedule);

        if (outcome.PeriodIsBilled)
        {
            var nextRun = cycleStart.AddMonths(1).AddDays(schedule.PublishDelayDays);
            schedule.GenerateAt = BillingCycle.LocalTime(nextRun, BillingCycle.RunTime);
            schedule.CompletedAt = now; schedule.LastAttemptAt = null; schedule.Result = outcome.Message;
            db.AuditLogs.Add(Audit(schedule, now, "Scheduled invoice run completed", outcome.Message));
            log.LogInformation("Scheduled billing {Start}–{End}: {Result}", periodStart, periodEnd, outcome.Message);
        }
        else
        {
            var message = $"Waiting for {periodStart:dd MMM yyyy} – {periodEnd:dd MMM yyyy}: {outcome.Message} (retried every hour; the month is not skipped)";
            if (schedule.Result != message) db.AuditLogs.Add(Audit(schedule, now, "Scheduled invoice run blocked", message));
            schedule.Result = message; schedule.LastAttemptAt = now;
            log.LogWarning("Scheduled billing {Start}–{End} blocked: {Result}", periodStart, periodEnd, outcome.Message);
        }
        await db.SaveChangesAsync(token);
        return outcome.PeriodIsBilled;
    }

    static AuditLog Audit(InvoiceSchedule schedule, DateTimeOffset now, string action, string value) => new()
    {
        Timestamp = now, UserId = schedule.CreatedBy, Action = action, EntityType = "InvoiceSchedule", EntityId = schedule.Id.ToString(),
        NewValue = value, Reason = "Automatic monthly billing", SourceIp = "Background service"
    };

    /// <summary>Creates the single active schedule, replacing any earlier one.</summary>
    public static async Task<InvoiceSchedule> ReplaceScheduleAsync(ApplicationDbContext db, TimeProvider time, int cycleStartDay, int publishDelayDays, int dueDays, string user)
    {
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).DateTime);
        var run = BillingCycle.FirstRunOnOrAfter(today, cycleStartDay, publishDelayDays);
        foreach (var item in await db.InvoiceSchedules.Where(x => !x.Completed).ToListAsync())
        { item.Completed = true; item.CompletedAt = now; item.Result = "Replaced by updated monthly settings."; }
        var schedule = new InvoiceSchedule
        {
            CycleStartDay = cycleStartDay, PublishDelayDays = publishDelayDays, DueDays = dueDays,
            GenerateAt = BillingCycle.LocalTime(run, BillingCycle.RunTime), CreatedAt = now, CreatedBy = user
        };
        db.InvoiceSchedules.Add(schedule);
        return schedule;
    }
}
