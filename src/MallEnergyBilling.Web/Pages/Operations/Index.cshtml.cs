using Controller = MallEnergyBilling.Web.Models.Controller;
using System.Text;
using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages.Operations;

public class IndexModel(ApplicationDbContext db, BillingRunService billing, TimeProvider time, PermissionService permissions) : PageModel
{
    /// <summary>Permissions of the signed-in user, for showing only the sections they may use.</summary>
    public HashSet<string> Allowed { get; private set; } = [];
    public bool Can(string permission) => Allowed.Contains(permission);
    async Task<bool> Has(string permission) => await permissions.HasAsync(User, permission);

    public List<Controller> Controllers { get; set; } = [];
    public List<Shop> Shops { get; set; } = [];
    public List<Meter> Meters { get; set; } = [];
    public List<Tariff> Tariffs { get; set; } = [];
    public List<MeterReading> Readings { get; set; } = [];
    public List<BillingPeriod> BillingPeriods { get; set; } = [];
    public List<Invoice> Invoices { get; set; } = [];
    public List<InvoiceSchedule> InvoiceSchedules { get; set; } = [];
    public InvoiceSchedule? ActiveSchedule => InvoiceSchedules.FirstOrDefault(x => !x.Completed);
    public BillingPreview? Preview { get; private set; }
    /// <summary>Readings stamped after the current time (the PC clock was moved forward and back).</summary>
    public List<MeterReading> FutureReadings { get; private set; } = [];
    /// <summary>Active meters that would stop the billing run because they have no tariff.</summary>
    public List<Meter> MetersWithoutTariff { get; private set; } = [];
    /// <summary>Meters that could not be billed for a period yet. The other meters of that period were billed.</summary>
    public List<PendingMeterBill> WaitingMeters { get; private set; } = [];
    /// <summary>Draft invoices waiting for approval, oldest period first.</summary>
    public List<Invoice> Drafts { get; private set; } = [];
    /// <summary>Monthly invoices are created as drafts and published only after approval.</summary>
    public bool ApprovalRequired { get; private set; } = true;
    public const int AllMetersWithoutTariff = -1, AllActiveMeters = -2;
    public DateOnly PreviewInvoiceDate { get; private set; }
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), TimeZoneInfo.Local).DateTime);
    public bool TestClockActive => time is OffsetTimeProvider;

    // Manual billing defaults to the last fully completed cycle, never the month in progress.
    public (DateOnly Start, DateOnly End) SuggestedPeriod => BillingCycle.LastCompletedPeriod(Today, ActiveSchedule?.CycleStartDay ?? 1);
    public DateOnly SuggestedPeriodStart => SuggestedPeriod.Start;
    public DateOnly SuggestedPeriodEnd => SuggestedPeriod.End;
    public DateOnly SuggestedInvoiceDate => Today;
    public DateOnly SuggestedDueDate => SuggestedInvoiceDate.AddDays(ActiveSchedule?.DueDays ?? 10);

    public IReadOnlyList<string>Currencies{get;}=["UGX","USD","EUR","GBP","AED","AFN","ALL","AMD","ANG","AOA","ARS","AUD","AWG","AZN","BAM","BBD","BDT","BGN","BHD","BIF","BMD","BND","BOB","BRL","BSD","BTN","BWP","BYN","BZD","CAD","CDF","CHF","CLP","CNY","COP","CRC","CUP","CVE","CZK","DJF","DKK","DOP","DZD","EGP","ERN","ETB","FJD","FKP","GEL","GHS","GIP","GMD","GNF","GTQ","GYD","HKD","HNL","HTG","HUF","IDR","ILS","INR","IQD","IRR","ISK","JMD","JOD","JPY","KES","KGS","KHR","KMF","KPW","KRW","KWD","KYD","KZT","LAK","LBP","LKR","LRD","LSL","LYD","MAD","MDL","MGA","MKD","MMK","MNT","MOP","MRU","MUR","MVR","MWK","MXN","MYR","MZN","NAD","NGN","NIO","NOK","NPR","NZD","OMR","PAB","PEN","PGK","PHP","PKR","PLN","PYG","QAR","RON","RSD","RUB","RWF","SAR","SBD","SCR","SDG","SEK","SGD","SHP","SLE","SOS","SRD","SSP","STN","SVC","SYP","SZL","THB","TJS","TMT","TND","TOP","TRY","TTD","TWD","TZS","UAH","UYU","UZS","VES","VND","VUV","WST","XAF","XCD","XOF","XPF","YER","ZAR","ZMW","ZWL"];

    public async Task OnGet()
    {
        foreach (var permission in Permissions.All) if (await Has(permission.Key)) Allowed.Add(permission.Key);
        Controllers = await db.Controllers.ToListAsync();
        Shops = await db.Shops.ToListAsync();
        Meters = await db.Meters.Include(x => x.Shop).Include(x => x.Controller).Where(x => x.CommunicationStatus != "Archived").ToListAsync();
        Tariffs = await db.Tariffs.Include(x => x.Meter).OrderByDescending(x => x.Id).ToListAsync();
        Readings = await db.MeterReadings.Include(x => x.Meter).OrderByDescending(x => x.Id).Take(100).ToListAsync();
        BillingPeriods = (await db.BillingPeriods.ToListAsync()).OrderByDescending(x => x.StartDate).ToList();
        InvoiceSchedules = (await db.InvoiceSchedules.ToListAsync()).OrderByDescending(x => x.GenerateAt).Take(20).ToList();
        Invoices = (await db.Invoices.Include(x => x.Meter).ThenInclude(x => x!.Shop).Include(x => x.Payments).ToListAsync()).OrderByDescending(x => x.InvoiceDate).ToList();
        foreach (var invoice in Invoices)
        {
            var status = AccountBalanceService.StatusAfterPayment(invoice, time.GetUtcNow());
            if (status != invoice.Status) invoice.Status = status;
        }
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
        await LoadChecksAsync();
    }

    async Task LoadChecksAsync()
    {
        FutureReadings = await ClockCheck.FutureReadingsAsync(db, time.GetUtcNow());
        var waterEnabled = await db.SystemFeatureConfigurations.AsNoTracking().AnyAsync(x => x.Id == 1 && x.WaterBillingEnabled);
        var priced = (await db.Tariffs.AsNoTracking().Where(x => x.MeterId != null).Select(x => x.MeterId!.Value).ToListAsync()).ToHashSet();
        MetersWithoutTariff = Meters.Where(x => x.Active && (waterEnabled || x.UtilityType != UtilityType.Water) && !priced.Contains(x.Id)).OrderBy(x => x.Name).ToList();
        ApprovalRequired = await billing.ApprovalRequiredAsync();
        Drafts = (await db.Invoices.AsNoTracking().Include(x => x.Meter).Include(x => x.Shop).Include(x => x.BillingPeriod).Where(x => x.Status == InvoiceStatus.Draft).ToListAsync())
            .OrderBy(x => x.BillingPeriod!.StartDate).ThenBy(x => x.Shop!.ShopNumber).ThenBy(x => x.Meter!.Name).ToList();
        WaitingMeters = (await db.PendingMeterBills.AsNoTracking().Include(x => x.Meter).ThenInclude(x => x!.Shop).Include(x => x.BillingPeriod).Where(x => x.ResolvedAt == null).ToListAsync())
            .OrderBy(x => x.BillingPeriod!.StartDate).ThenBy(x => x.Meter!.Name).ToList();
    }

    /// <summary>Rejects readings dated after now. They stay in the database but are never billed.</summary>
    public async Task<IActionResult> OnPostRejectFutureReadings()
    {
        if (!await Has(Permissions.RunBilling)) return Forbid();
        var count = await ClockCheck.RejectFutureReadingsAsync(db, time.GetUtcNow(), UserName("Administrator"), SourceIp());
        TempData["BillingSuccess"] = count == 0 ? "No readings are dated in the future." : $"{count} reading(s) dated in the future were rejected. They are kept in the history but never billed.";
        return RedirectToPage(null, null, "tariffs");
    }

    public async Task<IActionResult> OnPostTariff(int meterId, decimal price, string currency, string reason)
    {
        if (!await Has(Permissions.ManageTariffs)) return Forbid();
        if (price <= 0) return BadRequest("A positive price is required.");
        currency = (currency ?? "").Trim().ToUpperInvariant();
        if (!ValidCurrency(currency)) return BadRequest("Select a valid three-letter ISO currency code.");
        var meter = await db.Meters.FindAsync(meterId);
        if (meter is null) return BadRequest("Select a valid meter.");
        var auditReason = string.IsNullOrWhiteSpace(reason) ? "Not provided" : reason.Trim();
        var old = await db.Tariffs.Where(x => x.MeterId == meterId).OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        db.Tariffs.Add(new Tariff { MeterId = meterId, PricePerKwh = price, Currency = currency, EffectiveFrom = time.GetLocalNow(), CreatedAt = time.GetUtcNow(), ChangedBy = UserName("Local administrator"), Reason = auditReason });
        db.AuditLogs.Add(Audit("Meter tariff changed", "Meter", meterId.ToString(), old is null ? "none" : $"{old.Currency} {old.PricePerKwh}", $"{currency} {price}", auditReason));
        await db.SaveChangesAsync();
        TempData["BillingSuccess"] = $"Tariff saved for {meter.Name} and active from now.";
        return RedirectToPage(null, null, "tariffs");
    }

    public async Task<IActionResult> OnPostSaveTariffAndSchedule(int meterId, decimal price, string currency, int cycleStartDay, int publishDelayDays, int dueDays = 10, string reason = "")
    {
        if (!await Has(Permissions.ManageTariffs)) return Forbid();
        if (price <= 0) return BillingError("Enter a positive tariff price.");
        currency = (currency ?? "").Trim().ToUpperInvariant();
        if (!ValidCurrency(currency)) return BillingError("Select a valid currency.");
        if (ScheduleError(cycleStartDay, publishDelayDays, dueDays) is string error) return BillingError(error);
        List<Meter> targets;
        if (meterId is AllMetersWithoutTariff or AllActiveMeters)
        {
            var priced = (await db.Tariffs.Where(x => x.MeterId != null).Select(x => x.MeterId!.Value).ToListAsync()).ToHashSet();
            targets = await db.Meters.Where(x => x.Active && x.CommunicationStatus != "Archived").OrderBy(x => x.Id).ToListAsync();
            // Same meters the billing run uses: water meters only when water billing is switched on.
            if (!await db.SystemFeatureConfigurations.AsNoTracking().AnyAsync(x => x.Id == 1 && x.WaterBillingEnabled))
                targets = targets.Where(x => x.UtilityType != UtilityType.Water).ToList();
            if (meterId == AllMetersWithoutTariff) targets = targets.Where(x => !priced.Contains(x.Id)).ToList();
            if (targets.Count == 0) return BillingError(meterId == AllMetersWithoutTariff ? "Every active meter already has a tariff." : "There are no active meters.");
        }
        else
        {
            var meter = await db.Meters.FindAsync(meterId);
            if (meter is null) return BillingError("Select a valid meter.");
            targets = [meter];
        }
        var user = UserName("Administrator");
        var auditReason = string.IsNullOrWhiteSpace(reason) ? "Settings updated" : reason.Trim();
        var targetIds = targets.Select(x => (int?)x.Id).ToList();
        var oldTariffs = (await db.Tariffs.Where(x => targetIds.Contains(x.MeterId)).ToListAsync()).GroupBy(x => x.MeterId!.Value).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());
        await using var transaction = await db.Database.BeginTransactionAsync();
        var schedule = await InvoiceSchedulerService.ReplaceScheduleAsync(db, time, cycleStartDay, publishDelayDays, dueDays, user);
        foreach (var meter in targets)
        {
            db.Tariffs.Add(new() { MeterId = meter.Id, PricePerKwh = price, Currency = currency, EffectiveFrom = time.GetLocalNow(), CreatedAt = time.GetUtcNow(), ChangedBy = user, Reason = auditReason });
            db.AuditLogs.Add(Audit("Tariff and invoice schedule updated", "Meter", meter.Id.ToString(),
                oldTariffs.TryGetValue(meter.Id, out var oldTariff) ? $"{oldTariff.Currency} {oldTariff.PricePerKwh}" : "No tariff",
                $"{currency} {price}; billing cycle {BillingCycle.Describe(cycleStartDay)}; publish {publishDelayDays} day(s) after period end; due after {dueDays} days; next run {schedule.GenerateAt:yyyy-MM-dd}", auditReason));
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        var who = targets.Count == 1 ? targets[0].Name : $"{targets.Count} meters";
        TempData["BillingSuccess"] = $"{who}: tariff saved. Billing cycle: {BillingCycle.Describe(cycleStartDay)}. Next publication: {schedule.GenerateAt.ToLocalTime():dd MMM yyyy}.";
        return RedirectToPage(null, null, "tariffs");
    }

    public async Task<IActionResult> OnPostScheduleInvoices(int cycleStartDay, int publishDelayDays, int dueDays = 10)
    {
        if (!await Has(Permissions.ManageTariffs)) return Forbid();
        if (ScheduleError(cycleStartDay, publishDelayDays, dueDays) is string error) return BillingError(error);
        var schedule = await InvoiceSchedulerService.ReplaceScheduleAsync(db, time, cycleStartDay, publishDelayDays, dueDays, UserName("Administrator"));
        db.AuditLogs.Add(Audit("Monthly invoice publication scheduled", "InvoiceSchedule", "", "",
            $"Billing cycle {BillingCycle.Describe(cycleStartDay)}; publish {publishDelayDays} day(s) after period end; next run {schedule.GenerateAt:O}; due after {dueDays} days", "Automatic monthly billing"));
        await db.SaveChangesAsync();
        TempData["BillingSuccess"] = $"Billing cycle: {BillingCycle.Describe(cycleStartDay)}. Next publication: {schedule.GenerateAt.ToLocalTime():dd MMM yyyy}.";
        return RedirectToPage(null, null, "tariffs");
    }

    /// <summary>Shows exactly what a billing run would create, without saving anything.</summary>
    public async Task<IActionResult> OnPostPreview(DateOnly? asOf, DateOnly? periodStart, DateOnly? periodEnd)
    {
        if (!await Has(Permissions.RunBilling)) return Forbid();
        await OnGet();
        var schedule = ActiveSchedule;
        DateOnly start, end, invoiceDate;
        if (periodStart is DateOnly s && periodEnd is DateOnly e)
        {
            (start, end) = (s, e);
            invoiceDate = end.AddDays(1 + (schedule?.PublishDelayDays ?? 0));
        }
        else
        {
            var cycleStart = BillingCycle.LatestCycleStart(asOf ?? Today, schedule?.CycleStartDay ?? 1);
            (start, end) = BillingCycle.PeriodEndingBefore(cycleStart);
            invoiceDate = cycleStart.AddDays(schedule?.PublishDelayDays ?? 0);
        }
        PreviewInvoiceDate = invoiceDate;
        Preview = await billing.PreviewAsync(new BillingRunRequest(start, end, invoiceDate, invoiceDate.AddDays(schedule?.DueDays ?? 10)), allowUnfinishedPeriod: true, HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnPostGenerateInvoices(DateOnly periodStart, DateOnly periodEnd, DateOnly invoiceDate, DateOnly dueDate, decimal fixedCharge = 0, decimal otherCharge = 0, decimal taxPercent = 0, decimal discount = 0)
    {
        if (!await Has(Permissions.RunBilling)) return Forbid();
        var outcome = await billing.GenerateAsync(new BillingRunRequest(periodStart, periodEnd, invoiceDate, dueDate, fixedCharge, otherCharge, taxPercent, discount,
            Publish: false, User: UserName("Administrator"), SourceIp: SourceIp()), HttpContext.RequestAborted);
        if (!outcome.Created) return BillingError(outcome.AlreadyExists ? "This billing period already exists." : outcome.Message);
        TempData["BillingSuccess"] = $"{outcome.InvoiceCount} draft invoice(s) created. Review them, then finalize the billing period."
            + (outcome.WaitingCount == 0 ? "" : $" {outcome.WaitingCount} meter(s) could not be billed yet and are listed under Meters waiting to be billed.");
        return RedirectToPage(null, null, "billing");
    }

    /// <summary>Tries again now to bill the meters that are waiting (the service also does this every few minutes).</summary>
    public async Task<IActionResult> OnPostBillWaiting()
    {
        if (!await Has(Permissions.RunBilling)) return Forbid();
        var outcome = await billing.BillWaitingMetersAsync(UserName("Administrator"), SourceIp(), HttpContext.RequestAborted);
        TempData[outcome.Created ? "BillingSuccess" : "BillingError"] = outcome.Message;
        return RedirectToPage(null, null, "tariffs");
    }

    /// <summary>Decides that a waiting meter is not billed for that period.</summary>
    public async Task<IActionResult> OnPostDismissWaiting(int id, string reason)
    {
        if (!await Has(Permissions.RunBilling)) return Forbid();
        if (string.IsNullOrWhiteSpace(reason)) { TempData["BillingError"] = "Enter the reason why this meter is not billed."; return RedirectToPage(null, null, "tariffs"); }
        var done = await billing.DismissWaitingAsync(id, UserName("Administrator"), reason.Trim(), SourceIp(), HttpContext.RequestAborted);
        TempData[done ? "BillingSuccess" : "BillingError"] = done ? "The meter will not be billed for that period." : "That waiting meter was not found; it may already be billed.";
        return RedirectToPage(null, null, "tariffs");
    }

    /// <summary>Publishes all draft invoices of a billing period after review.</summary>
    public async Task<IActionResult> OnPostApproveDrafts(int periodId)
    {
        if (!await Has(Permissions.RunBilling)) return Forbid();
        var count = await billing.ApproveDraftsAsync(periodId, UserName("Administrator"), SourceIp(), HttpContext.RequestAborted);
        TempData[count > 0 ? "BillingSuccess" : "BillingError"] = count > 0 ? $"{count} invoice(s) approved and published. They are saved as PDF and, if automatic email is on, emailed." : "There were no draft invoices to approve.";
        return RedirectToPage(null, null, "tariffs");
    }

    /// <summary>Deletes a wrong draft; its meter waits (on hold) until the problem is fixed and Bill again is pressed.</summary>
    public async Task<IActionResult> OnPostRejectDraft(int invoiceId, string reason)
    {
        if (!await Has(Permissions.RunBilling)) return Forbid();
        if (string.IsNullOrWhiteSpace(reason)) return BillingError("Enter the reason why the draft is wrong.");
        var done = await billing.RejectDraftAsync(invoiceId, UserName("Administrator"), reason.Trim(), SourceIp(), HttpContext.RequestAborted);
        TempData[done ? "BillingSuccess" : "BillingError"] = done ? "The draft was removed. Fix the problem, then press Bill again under Meters waiting to be billed." : "That draft was not found; it may already be approved or removed.";
        return RedirectToPage(null, null, "tariffs");
    }

    /// <summary>Bills a meter whose draft was rejected again, after the problem was fixed.</summary>
    public async Task<IActionResult> OnPostBillAgain(int id)
    {
        if (!await Has(Permissions.RunBilling)) return Forbid();
        var outcome = await billing.BillAgainAsync(id, UserName("Administrator"), SourceIp(), HttpContext.RequestAborted);
        TempData[outcome.Created ? "BillingSuccess" : "BillingError"] = outcome.Message;
        return RedirectToPage(null, null, "tariffs");
    }

    /// <summary>Turns approval before publishing on or off.</summary>
    public async Task<IActionResult> OnPostApprovalSetting(bool approve)
    {
        if (!await Has(Permissions.RunBilling)) return Forbid();
        var settings = await db.SystemFeatureConfigurations.FirstOrDefaultAsync(x => x.Id == 1);
        if (settings is null) { settings = new SystemFeatureConfiguration { Id = 1 }; db.SystemFeatureConfigurations.Add(settings); }
        var previous = settings.ApproveInvoicesBeforePublishing;
        settings.ApproveInvoicesBeforePublishing = approve; settings.UpdatedAt = time.GetUtcNow(); settings.UpdatedBy = UserName("Administrator");
        db.AuditLogs.Add(Audit("Invoice approval setting changed", "SystemFeatureConfiguration", "1", $"Approve before publishing={previous}", $"Approve before publishing={approve}", "Billing publication setting"));
        await db.SaveChangesAsync();
        TempData["BillingSuccess"] = approve ? "Monthly invoices will be created as drafts and published only after you approve them." : "Monthly invoices will be published automatically, without approval.";
        return RedirectToPage(null, null, "tariffs");
    }

    public async Task<IActionResult> OnPostFinalizeInvoices(int periodId)
    {
        if (!await Has(Permissions.RunBilling)) return Forbid();
        var period = await db.BillingPeriods.FindAsync(periodId);
        if (period is null) return BillingError("Billing period was not found.");
        if (period.Status is BillingStatus.Finalized or BillingStatus.Closed) return BillingError("This billing period is already finalized.");
        var invoices = await db.Invoices.Where(x => x.BillingPeriodId == periodId).ToListAsync();
        if (invoices.Count == 0) return BillingError("There are no invoices to finalize.");
        var autoEmail = await db.SmtpConfigurations.AsNoTracking().AnyAsync(x => x.Enabled && x.AutoSendPublishedInvoices);
        foreach (var invoice in invoices.Where(x => x.Status == InvoiceStatus.Draft)) { invoice.Status = InvoiceStatus.Finalized; invoice.Locked = true; invoice.AutoEmailRequested = autoEmail; }
        period.Status = BillingStatus.Finalized; period.FinalizedAt = time.GetUtcNow();
        db.AuditLogs.Add(Audit("Invoices finalized", "BillingPeriod", period.Id.ToString(), "", $"{invoices.Count} invoices locked; automatic email={autoEmail}", "Billing period approved"));
        await db.SaveChangesAsync();
        TempData["BillingSuccess"] = $"Billing period {period.Number} finalized. Its invoices are now published and locked.";
        return RedirectToPage(null, null, "billing");
    }

    public async Task<IActionResult> OnPostRecordPayment(int invoiceId, decimal amount, DateOnly paymentDate, string method, string referenceNumber, string notes)
    {
        if (!await Has(Permissions.RecordPayments)) return Forbid();
        var invoice = await db.Invoices.Include(x => x.Payments).FirstOrDefaultAsync(x => x.Id == invoiceId);
        if (invoice is null) return BillingError("Invoice was not found.");
        if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Cancelled) return BillingError("Only published invoices can receive payments.");
        if (amount <= 0 || amount > invoice.Balance) return BillingError("Payment must be positive and cannot exceed the invoice balance.");
        var payment = new Payment { InvoiceId = invoice.Id, PaymentDate = BillingCycle.LocalStart(paymentDate), Amount = amount, Currency = invoice.Currency, Method = string.IsNullOrWhiteSpace(method) ? "Cash" : method.Trim(), ReferenceNumber = (referenceNumber ?? "").Trim(), ReceivedBy = UserName("Administrator"), Notes = (notes ?? "").Trim() };
        db.Payments.Add(payment); invoice.Payments.Add(payment);
        invoice.Status = AccountBalanceService.StatusAfterPayment(invoice, time.GetUtcNow());
        db.AuditLogs.Add(Audit("Payment recorded", "Invoice", invoice.Id.ToString(), "", $"{invoice.Currency} {amount:N2} by {payment.Method}", string.IsNullOrWhiteSpace(payment.Notes) ? "Customer payment" : payment.Notes));
        await db.SaveChangesAsync();
        TempData["BillingSuccess"] = $"Payment recorded for {invoice.InvoiceNumber}.";
        return RedirectToPage(null, null, "billing");
    }

    public async Task<IActionResult> OnGetCsv()
    {
        if (!await Has(Permissions.ExportReadings)) return Forbid();
        var rows = await db.MeterReadings.AsNoTracking().Include(x => x.Meter).OrderBy(x => x.Id).ToListAsync();
        var text = new StringBuilder("Timestamp,Meter,Raw,AccumulatedKwh,Source,Quality\r\n");
        foreach (var x in rows) text.Append($"{x.Timestamp:O},{Csv(x.Meter?.Name)},{x.RawValue},{x.AccumulatedKwh},{x.Source},{Csv(x.Quality)}\r\n");
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray(), "text/csv", "meter-readings.csv");
    }

    /// <summary>Quotes a CSV field when needed and neutralises spreadsheet formulas (=, +, -, @).</summary>
    public static string Csv(string? value)
    {
        value ??= "";
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }





    static string? ScheduleError(int cycleStartDay, int publishDelayDays, int dueDays) =>
        !BillingCycle.IsValidStartDay(cycleStartDay) ? $"Select a billing cycle start day from {BillingCycle.MinStartDay} to {BillingCycle.MaxStartDay}."
        : !BillingCycle.IsValidPublishDelay(publishDelayDays) ? $"Publish invoices 0 to {BillingCycle.MaxPublishDelayDays} days after the period ends."
        : dueDays is < 0 or > 365 ? "Payment due days must be between 0 and 365." : null;

    static bool ValidCurrency(string currency) => currency.Length == 3 && currency.All(c => c is >= 'A' and <= 'Z');
    string UserName(string fallback) => User.Identity?.Name ?? fallback;
    string SourceIp() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    AuditLog Audit(string action, string entityType, string entityId, string oldValue, string newValue, string reason) => new()
    { Timestamp = time.GetUtcNow(), UserId = UserName("Administrator"), Action = action, EntityType = entityType, EntityId = entityId, OldValue = oldValue, NewValue = newValue, Reason = reason, SourceIp = SourceIp() };
    IActionResult BillingError(string message) { TempData["BillingError"] = message; return RedirectToPage(null, null, "tariffs"); }
}
