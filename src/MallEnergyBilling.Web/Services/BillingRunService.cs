using System.Text.Json;
using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Services;

public sealed record BillingRunRequest(
    DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly InvoiceDate, DateOnly DueDate,
    decimal FixedCharge = 0, decimal OtherCharge = 0, decimal TaxPercent = 0, decimal Discount = 0,
    bool Publish = true, string User = "System", string SourceIp = "Background service", string Reason = "Monthly billing run");

/// <param name="CoversFrom">First day of the usage on this invoice. Earlier than the period start when a month
/// that could not be billed for this meter is included.</param>
public sealed record BillingLine(
    int MeterId, string MeterName, string ShopName, string Unit,
    decimal Opening, decimal Closing, DateTimeOffset? OpeningAt, DateTimeOffset ClosingAt,
    decimal TariffPerUnit, string Currency, decimal PreviousBalance, BillingResult Result,
    long? OpeningReadingId, long ClosingReadingId, string Note, DateOnly CoversFrom);

/// <summary>A meter that cannot be billed for the period. It does not stop the other meters.</summary>
public sealed record MeterProblem(int MeterId, string MeterName, string ShopName, string Reason);

/// <param name="Errors">Problems with the run itself (dates, overlapping periods). They stop every invoice.</param>
/// <param name="Problems">Problems with single meters. Only that meter waits; every other meter is billed.</param>
public sealed record BillingPreview(
    DateOnly PeriodStart, DateOnly PeriodEnd, IReadOnlyList<BillingLine> Lines,
    IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, int? ExistingPeriodId, IReadOnlyList<MeterProblem> Problems)
{
    public bool CanGenerate => Errors.Count == 0 && (Lines.Count > 0 || Problems.Count > 0) && ExistingPeriodId is null;
}

public sealed record BillingRunOutcome(bool Created, bool AlreadyExists, string Message, int? PeriodId = null, int InvoiceCount = 0, int WaitingCount = 0)
{
    /// <summary>True when the period is billed (now or earlier) and a schedule may move on to the next month.</summary>
    public bool PeriodIsBilled => Created || AlreadyExists;
}

/// <summary>
/// The single implementation of a billing run, shared by the automatic monthly schedule,
/// the manual run and the preview. It never invents consumption. Each meter is handled on its own:
/// a meter with a problem (no tariff, no reading, a counter decrease to review) is not billed and is
/// recorded as waiting, while every other meter is billed normally. A waiting meter is billed for its
/// period as soon as the problem is fixed, or its usage is included in its next invoice.
/// </summary>
public sealed class BillingRunService(ApplicationDbContext db, BillingCalculator calculator, TariffResolver tariffResolver, TimeProvider time)
{
    public const string BaselineQuality = "Baseline";
    public const string RejectedQuality = "Rejected";
    static readonly TimeSpan ClosingStaleWarning = TimeSpan.FromHours(24);

    sealed record Row(long Id, int MeterId, decimal Value, DateTimeOffset Timestamp, string Quality, bool RequiresReview);

    /// <summary>Calculates every invoice the run would create, without saving anything.</summary>
    /// <param name="allowUnfinishedPeriod">Preview only: show a period that has not ended yet as a warning instead of an error.</param>
    public async Task<BillingPreview> PreviewAsync(BillingRunRequest request, bool allowUnfinishedPeriod = false, CancellationToken ct = default)
    {
        var errors = new List<string>(); var warnings = new List<string>(); var lines = new List<BillingLine>(); var problems = new List<MeterProblem>();
        var (periodStart, periodEnd) = (request.PeriodStart, request.PeriodEnd);
        if (periodEnd < periodStart) return new(periodStart, periodEnd, lines, ["Period end cannot be before period start."], warnings, null, problems);
        if (request.InvoiceDate < periodEnd) errors.Add("Invoice date must be on or after the consumption period end.");
        if (request.DueDate < request.InvoiceDate) errors.Add("Due date cannot be before the invoice date.");
        if (request.FixedCharge < 0 || request.OtherCharge < 0 || request.Discount < 0 || request.TaxPercent is < 0 or > 100)
            errors.Add("Charges and discount cannot be negative, and tax must be between 0 and 100%.");

        var endExclusive = BillingCycle.LocalStart(periodEnd.AddDays(1));
        var issued = BillingCycle.LocalStart(request.InvoiceDate);
        if (endExclusive > time.GetUtcNow())
        {
            var message = $"The period ends on {periodEnd:dd MMM yyyy}. Invoices can only be generated after the whole period has finished, otherwise the remaining days would never be billed.";
            if (allowUnfinishedPeriod) warnings.Add("Preview of an unfinished period: readings so far only. " + message); else errors.Add(message);
        }

        var periods = await db.BillingPeriods.AsNoTracking().ToListAsync(ct);
        var existing = periods.FirstOrDefault(x => x.StartDate == periodStart && x.EndDate == periodEnd);
        foreach (var other in periods.Where(x => x != existing && x.StartDate <= periodEnd && x.EndDate >= periodStart))
            errors.Add($"The period overlaps billing period {other.Number} ({other.StartDate:dd MMM yyyy} – {other.EndDate:dd MMM yyyy}); the overlapping days would be billed twice.");
        if (existing is not null) warnings.Add($"Billing period {existing.Number} for these dates already exists.");

        var meters = await BillableMetersAsync(null, ct);
        if (meters.Count == 0) errors.Add("There are no active meters to bill.");
        var data = await LoadAsync(meters.Select(x => x.Id).ToList(), ct);
        // A meter still waiting from an earlier month is billed from the start of that month, so its usage is not lost.
        var waitingSince = (await db.PendingMeterBills.AsNoTracking().Include(x => x.BillingPeriod).Where(x => x.ResolvedAt == null).ToListAsync(ct))
            .Where(x => x.BillingPeriod!.StartDate < periodStart).GroupBy(x => x.MeterId).ToDictionary(g => g.Key, g => g.Min(x => x.BillingPeriod!.StartDate));

        foreach (var meter in meters)
        {
            var from = waitingSince.TryGetValue(meter.Id, out var since) ? since : periodStart;
            var (line, problem) = Evaluate(meter, from, periodStart, periodEnd, issued, request, data, warnings);
            if (line is not null) lines.Add(line);
            else problems.Add(new(meter.Id, meter.Name, meter.Shop?.Name ?? "", problem!));
        }
        return new(periodStart, periodEnd, lines, errors, warnings, existing?.Id, problems);
    }

    sealed record RunData(ILookup<int, Row> Readings, List<Tariff> Tariffs, List<Invoice> PriorInvoices);

    async Task<List<Meter>> BillableMetersAsync(IReadOnlyCollection<int>? ids, CancellationToken ct)
    {
        var query = db.Meters.AsNoTracking().Include(x => x.Shop).Include(x => x.Controller).Where(x => x.Active);
        if (ids is not null) query = query.Where(x => ids.Contains(x.Id));
        var meters = await query.OrderBy(x => x.Id).ToListAsync(ct);
        var waterEnabled = await db.SystemFeatureConfigurations.AsNoTracking().AnyAsync(x => x.Id == 1 && x.WaterBillingEnabled, ct);
        return waterEnabled ? meters : meters.Where(x => x.UtilityType != UtilityType.Water).ToList();
    }

    async Task<RunData> LoadAsync(List<int> meterIds, CancellationToken ct)
    {
        // SQLite cannot compare DateTimeOffset values in SQL, so only a small projection is loaded and filtered in memory.
        var readings = (await db.MeterReadings.AsNoTracking().Where(x => meterIds.Contains(x.MeterId))
                .Select(x => new Row(x.Id, x.MeterId, x.AccumulatedKwh, x.Timestamp, x.Quality, x.RequiresReview)).ToListAsync(ct))
            .OrderBy(x => x.Timestamp).ThenBy(x => x.Id).ToLookup(x => x.MeterId);
        return new(readings, await db.Tariffs.AsNoTracking().ToListAsync(ct),
            await db.Invoices.AsNoTracking().Include(x => x.Payments).Include(x => x.BillingPeriod).ToListAsync(ct));
    }

    /// <summary>Bills one meter for the usage from <paramref name="from"/> to the end of the period, or says why it cannot.</summary>
    (BillingLine? Line, string? Problem) Evaluate(Meter meter, DateOnly from, DateOnly periodStart, DateOnly periodEnd, DateTimeOffset issued, BillingRunRequest request, RunData data, List<string> warnings)
    {
        var start = BillingCycle.LocalStart(from);
        var endExclusive = BillingCycle.LocalStart(periodEnd.AddDays(1));
        var rows = data.Readings[meter.Id].Where(x => x.Timestamp < endExclusive).ToList();
        var usable = rows.Where(x => !x.RequiresReview && x.Quality != RejectedQuality).ToList();
        var openReview = rows.Where(x => x.RequiresReview).FirstOrDefault(review => !Recovered(review, usable));
        if (meter.BacnetCounterReview || openReview is not null)
        {
            var when = openReview is null ? "" : $" on {openReview.Timestamp.ToLocalTime():dd MMM yyyy HH:mm}";
            return (null, $"counter decrease{when} needs review. Open the meter page and choose Ignore reading or Accept new starting value.");
        }
        var opening = usable.LastOrDefault(x => x.Timestamp < start);
        var closing = usable.LastOrDefault(x => x.Timestamp >= start);
        var bacnetError = BacnetBillingGuard.Validate(meter, [], closing is null ? null : new MeterReading { Quality = closing.Quality, Timestamp = closing.Timestamp }, endExclusive);
        if (bacnetError is not null) return (null, bacnetError);
        if (closing is null) return (null, "no reading in the period (check the meter's communication). Its usage will be billed once readings arrive.");
        if (endExclusive - closing.Timestamp > ClosingStaleWarning && meter.Controller?.CommunicationType != "BacnetIp")
            warnings.Add($"{meter.Name}: the last reading in the period is from {closing.Timestamp.ToLocalTime():dd MMM yyyy HH:mm}; usage after it will be billed next period.");

        // Walk the period readings so a meter reset/replacement (baseline reading) is billed correctly.
        var previous = opening?.Value ?? meter.InitialReading; var consumption = 0m; var resets = new List<DateTimeOffset>();
        foreach (var row in usable.Where(x => x.Timestamp >= start))
        {
            if (row.Quality == BaselineQuality) { previous = row.Value; resets.Add(row.Timestamp); continue; }
            if (row.Value < previous) return (null, $"reading fell from {previous:N3} to {row.Value:N3} on {row.Timestamp.ToLocalTime():dd MMM yyyy HH:mm} without a review");
            consumption += row.Value - previous; previous = row.Value;
        }

        // Price the period with the tariff in force at its end, never one set after the period.
        Tariff tariff;
        try { tariff = tariffResolver.Resolve(data.Tariffs, meter.Id, endExclusive.AddTicks(-1)); }
        catch (InvalidOperationException)
        {
            try
            {
                tariff = tariffResolver.Resolve(data.Tariffs, meter.Id, issued);
                warnings.Add($"{meter.Name}: no tariff was in force during the period; using the tariff set on {tariff.EffectiveFrom.ToLocalTime():dd MMM yyyy}.");
            }
            catch (InvalidOperationException) { return (null, "no tariff. Set a tariff for this meter."); }
        }

        // Arrears belong to this meter at this shop; a meter moved to another shop starts clean.
        var previousBalance = AccountBalanceService.Outstanding(data.PriorInvoices.Where(x => x.MeterId == meter.Id && x.ShopId == meter.ShopId));
        BillingResult result;
        try
        {
            result = calculator.Calculate(new(opening?.Value ?? meter.InitialReading, closing.Value, tariff.PricePerKwh,
                request.FixedCharge, request.OtherCharge, request.TaxPercent, request.Discount, previousBalance, consumption));
        }
        catch (InvalidOperationException ex) { return (null, ex.Message); }
        var notes = new List<string>();
        if (from < periodStart)
            notes.Add($"This invoice also includes the usage from {from:dd MMM yyyy} to {periodStart.AddDays(-1):dd MMM yyyy}, which could not be billed at that time.");
        if (resets.Count > 0)
            notes.Add($"The meter counter was restarted {(resets.Count == 1 ? "on" : $"{resets.Count} times, from")} {resets[0].ToLocalTime():dd MMM yyyy HH:mm}{(resets.Count == 1 ? "" : $" to {resets[^1].ToLocalTime():dd MMM yyyy HH:mm}")} (meter replaced or its settings changed). Consumption is the usage measured before and after the restart, so it is not simply current minus previous reading.");
        return (new(meter.Id, meter.Name, meter.Shop?.Name ?? "", meter.Unit, opening?.Value ?? meter.InitialReading, closing.Value,
            opening?.Timestamp, closing.Timestamp, tariff.PricePerKwh, tariff.Currency, previousBalance, result, opening?.Id, closing.Id, string.Join(" ", notes), from), null);
    }

    /// <summary>
    /// A suspect (decreasing) reading followed by a good reading at or above the last accepted value
    /// was a transient communication glitch: the counter recovered, so it does not block billing.
    /// </summary>
    static bool Recovered(Row review, IReadOnlyList<Row> usable)
    {
        var before = usable.LastOrDefault(x => x.Timestamp <= review.Timestamp);
        return before is not null && usable.Any(x => x.Timestamp > review.Timestamp && x.Quality != BaselineQuality && x.Value >= before.Value);
    }

    /// <summary>
    /// Creates the billing period with an invoice for every meter that is ready, in one transaction.
    /// Meters that are not ready are recorded as waiting; they never stop the other invoices.
    /// </summary>
    public async Task<BillingRunOutcome> GenerateAsync(BillingRunRequest request, CancellationToken ct = default)
    {
        var preview = await PreviewAsync(request, false, ct);
        if (preview.ExistingPeriodId is int existingId)
            return new(false, true, $"Skipped: billing period {request.PeriodStart:dd MMM yyyy} – {request.PeriodEnd:dd MMM yyyy} already exists.", existingId);
        if (preview.Errors.Count > 0) return new(false, false, "No invoices created: " + string.Join("; ", preview.Errors));

        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var now = time.GetUtcNow();
        var period = new BillingPeriod
        {
            Number = $"BP-{request.PeriodStart:yyyyMM}", StartDate = request.PeriodStart, EndDate = request.PeriodEnd,
            Status = request.Publish ? BillingStatus.Finalized : BillingStatus.Calculated, CreatedAt = now,
            FinalizedAt = request.Publish ? now : null, ResponsibleUser = request.User
        };
        db.BillingPeriods.Add(period);
        await db.SaveChangesAsync(ct);

        var autoEmail = request.Publish && await db.SmtpConfigurations.AsNoTracking().AnyAsync(x => x.Enabled && x.AutoSendPublishedInvoices, ct);
        var next = await NextInvoiceNumberAsync(ct);
        var created = await AddInvoicesAsync(preview.Lines, period, request, BillingCycle.LocalStart(request.InvoiceDate), BillingCycle.LocalStart(request.DueDate), request.Publish, autoEmail, next, now, ct);
        var dueDays = request.DueDate.DayNumber - request.InvoiceDate.DayNumber;
        foreach (var problem in preview.Problems)
            db.PendingMeterBills.Add(new PendingMeterBill
            {
                BillingPeriodId = period.Id, MeterId = problem.MeterId, Reason = problem.Reason, CreatedAt = now, LastCheckedAt = now,
                FixedCharge = request.FixedCharge, OtherCharge = request.OtherCharge, TaxPercent = request.TaxPercent, Discount = request.Discount, DueDays = dueDays
            });
        var waiting = preview.Problems.Count == 0 ? "" : $"; {preview.Problems.Count} meter(s) waiting: " + string.Join("; ", preview.Problems.Select(x => $"{x.MeterName}: {x.Reason.TrimEnd('.')}"));
        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = now, UserId = request.User, Action = request.Publish ? "Invoices published" : "Draft invoices generated",
            EntityType = "BillingPeriod", EntityId = period.Id.ToString(),
            NewValue = $"{created.Count} invoices for {request.PeriodStart:yyyy-MM-dd} – {request.PeriodEnd:yyyy-MM-dd}; invoice date {request.InvoiceDate:yyyy-MM-dd}; due {request.DueDate:yyyy-MM-dd}; automatic email={autoEmail}{waiting}",
            Reason = request.Reason, SourceIp = request.SourceIp
        });
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return new(true, false, $"{created.Count} invoice(s) {(request.Publish ? "published" : "created as drafts")} for {request.PeriodStart:dd MMM yyyy} – {request.PeriodEnd:dd MMM yyyy}{waiting}.",
            period.Id, created.Count, preview.Problems.Count);
    }

    /// <summary>
    /// Tries again to bill every meter that is waiting. A meter whose problem is fixed gets its invoice in
    /// the billing period it belongs to, dated today. A meter waiting for several months gets one invoice,
    /// in the latest of them, covering all of them. Meters that are still not ready keep waiting.
    /// </summary>
    public async Task<BillingRunOutcome> BillWaitingMetersAsync(string user = "System", string sourceIp = "Background service", CancellationToken ct = default)
    {
        var open = await db.PendingMeterBills.Include(x => x.BillingPeriod).Where(x => x.ResolvedAt == null).ToListAsync(ct);
        if (open.Count == 0) return new(false, false, "No meter is waiting to be billed.");
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).DateTime);
        var meters = (await BillableMetersAsync(open.Select(x => x.MeterId).Distinct().ToList(), ct)).ToDictionary(x => x.Id);
        var data = await LoadAsync(meters.Keys.ToList(), ct);
        var autoEmail = await db.SmtpConfigurations.AsNoTracking().AnyAsync(x => x.Enabled && x.AutoSendPublishedInvoices, ct);
        var next = await NextInvoiceNumberAsync(ct);
        var created = new List<Invoice>(); var stillWaiting = 0;
        foreach (var group in open.GroupBy(x => x.MeterId))
        {
            var items = group.OrderBy(x => x.BillingPeriod!.StartDate).ToList();
            var latest = items[^1]; var period = latest.BillingPeriod!;
            void Wait(string reason) { foreach (var item in items) { item.Reason = reason; item.LastCheckedAt = now; } stillWaiting++; }
            if (!meters.TryGetValue(group.Key, out var meter)) { Wait("the meter is not active (or water billing is off). Activate it to bill it, or choose Do not bill."); continue; }
            if (data.PriorInvoices.Any(x => x.MeterId == meter.Id && x.BillingPeriod is { } p && p.StartDate >= period.StartDate))
            { Wait("this meter already has an invoice for this or a later period. Check it, then choose Do not bill."); continue; }
            var warnings = new List<string>();
            var request = new BillingRunRequest(items[0].BillingPeriod!.StartDate, period.EndDate, today, today.AddDays(latest.DueDays),
                latest.FixedCharge, latest.OtherCharge, latest.TaxPercent, latest.Discount);
            var (line, problem) = Evaluate(meter, items[0].BillingPeriod!.StartDate, period.StartDate, period.EndDate, now, request, data, warnings); // a tariff set "from now" counts
            if (line is null) { Wait(problem!); continue; }
            var publish = period.Status is BillingStatus.Finalized or BillingStatus.Closed;
            var invoice = (await AddInvoicesAsync([line], period, request, BillingCycle.LocalStart(today), BillingCycle.LocalStart(request.DueDate), publish, publish && autoEmail, next++, now, ct))[0];
            created.Add(invoice);
            foreach (var item in items) { item.ResolvedAt = now; item.LastCheckedAt = now; item.Resolution = $"Billed on {invoice.InvoiceNumber}"; }
        }
        if (created.Count > 0)
            db.AuditLogs.Add(new AuditLog
            {
                Timestamp = now, UserId = user, Action = "Waiting meters billed", EntityType = "Invoice", EntityId = string.Join(",", created.Select(x => x.InvoiceNumber)),
                NewValue = $"{created.Count} invoice(s) dated {today:yyyy-MM-dd} for meters that were waiting; {stillWaiting} meter(s) still waiting",
                Reason = "Meter problem fixed", SourceIp = sourceIp
            });
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return new(created.Count > 0, false, created.Count == 0 ? $"{stillWaiting} meter(s) still waiting; nothing could be billed yet."
            : $"{created.Count} invoice(s) created for meters that were waiting ({string.Join(", ", created.Select(x => x.InvoiceNumber))}); {stillWaiting} still waiting.", null, created.Count, stillWaiting);
    }

    /// <summary>Adds the invoices, marks their readings as used and closes any earlier waiting entry of the same meters.</summary>
    async Task<List<Invoice>> AddInvoicesAsync(IReadOnlyList<BillingLine> lines, BillingPeriod period, BillingRunRequest request, DateTimeOffset issued, DateTimeOffset due,
        bool publish, bool autoEmail, int firstNumber, DateTimeOffset now, CancellationToken ct)
    {
        var usedIds = lines.SelectMany(x => x.OpeningReadingId is long id ? new[] { id, x.ClosingReadingId } : new[] { x.ClosingReadingId }).ToList();
        foreach (var reading in await db.MeterReadings.Where(x => usedIds.Contains(x.Id)).ToListAsync(ct)) reading.UsedForBilling = true;
        var meterIds = lines.Select(l => l.MeterId).ToList();
        var shops = await db.Meters.AsNoTracking().Where(x => meterIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.ShopId, ct);
        var invoices = new List<Invoice>();
        foreach (var line in lines)
        {
            var invoice = new Invoice
            {
                InvoiceNumber = $"INV-{firstNumber++:000000}", BillingPeriodId = period.Id, MeterId = line.MeterId, ShopId = shops[line.MeterId],
                InvoiceDate = issued, DueDate = due, OpeningReading = line.Opening, ClosingReading = line.Closing,
                ConsumptionKwh = line.Result.Consumption, TariffPerKwh = line.TariffPerUnit, EnergyCharge = line.Result.EnergyCharge,
                FixedCharges = request.FixedCharge, OtherCharges = request.OtherCharge, Tax = line.Result.Tax, Discount = request.Discount,
                PreviousBalance = line.PreviousBalance, Total = line.Result.Total, Currency = line.Currency,
                Status = publish ? InvoiceStatus.Finalized : InvoiceStatus.Draft, Locked = publish,
                AutoEmailRequested = autoEmail, Notes = line.Note,
                SnapshotJson = JsonSerializer.Serialize(new { PeriodStart = line.CoversFrom, PeriodEnd = period.EndDate, Tariff = line.TariffPerUnit, line.Currency, line.OpeningAt, line.ClosingAt, line.Note })
            };
            db.Invoices.Add(invoice);
            invoices.Add(invoice);
        }
        // The usage of an earlier waiting month is on these invoices now.
        var earlier = await db.PendingMeterBills.Where(x => x.ResolvedAt == null && meterIds.Contains(x.MeterId) && x.BillingPeriodId != period.Id).ToListAsync(ct);
        foreach (var item in earlier)
        {
            var invoice = invoices.First(x => x.MeterId == item.MeterId);
            item.ResolvedAt = now; item.LastCheckedAt = now; item.Resolution = $"Included in {invoice.InvoiceNumber} ({period.Number})";
        }
        return invoices;
    }

    /// <summary>Marks a waiting meter as not to be billed for that period. Its usage for the period is then never billed.</summary>
    public async Task<bool> DismissWaitingAsync(int id, string user, string reason, string sourceIp, CancellationToken ct = default)
    {
        var item = await db.PendingMeterBills.Include(x => x.Meter).Include(x => x.BillingPeriod).FirstOrDefaultAsync(x => x.Id == id && x.ResolvedAt == null, ct);
        if (item is null) return false;
        var now = time.GetUtcNow();
        item.ResolvedAt = now; item.LastCheckedAt = now; item.Resolution = $"Not billed, by {user}: {reason}";
        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = now, UserId = user, Action = "Waiting meter not billed", EntityType = "PendingMeterBill", EntityId = item.Id.ToString(),
            OldValue = $"{item.Meter?.Name} waiting for {item.BillingPeriod?.Number}: {item.Reason}", NewValue = "Not billed", Reason = reason, SourceIp = sourceIp
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Next invoice number. Never reuses a number, even after invoices were deleted.</summary>
    async Task<int> NextInvoiceNumberAsync(CancellationToken ct)
    {
        var highest = (await db.Invoices.Select(x => x.InvoiceNumber).ToListAsync(ct))
            .Select(x => x.StartsWith("INV-", StringComparison.Ordinal) && int.TryParse(x[4..], out var n) ? n : 0).DefaultIfEmpty(0).Max();
        long sequence = 0;
        try { sequence = await db.Database.SqlQueryRaw<long>("SELECT seq AS \"Value\" FROM sqlite_sequence WHERE name = 'Invoices'").FirstOrDefaultAsync(ct); }
        catch (Microsoft.Data.Sqlite.SqliteException) { /* no rows inserted yet */ }
        return (int)Math.Max(highest, sequence) + 1;
    }
}
