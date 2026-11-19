using System.Text.Json;
using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Services;

public sealed record BillingRunRequest(
    DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly InvoiceDate, DateOnly DueDate,
    decimal FixedCharge = 0, decimal OtherCharge = 0, decimal TaxPercent = 0, decimal Discount = 0,
    bool Publish = true, string User = "System", string SourceIp = "Background service", string Reason = "Monthly billing run");

public sealed record BillingLine(
    int MeterId, string MeterName, string ShopName, string Unit,
    decimal Opening, decimal Closing, DateTimeOffset? OpeningAt, DateTimeOffset ClosingAt,
    decimal TariffPerUnit, string Currency, decimal PreviousBalance, BillingResult Result,
    long? OpeningReadingId, long ClosingReadingId, string Note);

public sealed record BillingPreview(
    DateOnly PeriodStart, DateOnly PeriodEnd, IReadOnlyList<BillingLine> Lines,
    IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, int? ExistingPeriodId)
{
    public bool CanGenerate => Errors.Count == 0 && Lines.Count > 0 && ExistingPeriodId is null;
}

public sealed record BillingRunOutcome(bool Created, bool AlreadyExists, string Message, int? PeriodId = null, int InvoiceCount = 0)
{
    /// <summary>True when the period is billed (now or earlier) and a schedule may move on to the next month.</summary>
    public bool PeriodIsBilled => Created || AlreadyExists;
}

/// <summary>
/// The single implementation of a billing run, shared by the automatic monthly schedule,
/// the manual run and the preview. It never invents consumption: suspect counter readings,
/// unfinished periods and overlapping periods stop the run with a clear reason.
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
        var errors = new List<string>(); var warnings = new List<string>(); var lines = new List<BillingLine>();
        var (periodStart, periodEnd) = (request.PeriodStart, request.PeriodEnd);
        if (periodEnd < periodStart) return new(periodStart, periodEnd, lines, ["Period end cannot be before period start."], warnings, null);
        if (request.InvoiceDate < periodEnd) errors.Add("Invoice date must be on or after the consumption period end.");
        if (request.DueDate < request.InvoiceDate) errors.Add("Due date cannot be before the invoice date.");
        if (request.FixedCharge < 0 || request.OtherCharge < 0 || request.Discount < 0 || request.TaxPercent is < 0 or > 100)
            errors.Add("Charges and discount cannot be negative, and tax must be between 0 and 100%.");

        var start = BillingCycle.LocalStart(periodStart);
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

        var meters = await db.Meters.AsNoTracking().Include(x => x.Shop).Include(x => x.Controller).Where(x => x.Active).OrderBy(x => x.Id).ToListAsync(ct);
        var waterEnabled = await db.SystemFeatureConfigurations.AsNoTracking().AnyAsync(x => x.Id == 1 && x.WaterBillingEnabled, ct);
        if (!waterEnabled) meters = meters.Where(x => x.UtilityType != UtilityType.Water).ToList();
        if (meters.Count == 0) errors.Add("There are no active meters to bill.");

        var meterIds = meters.Select(x => x.Id).ToList();
        // SQLite cannot compare DateTimeOffset values in SQL, so only a small projection is loaded and filtered here.
        var readings = (await db.MeterReadings.AsNoTracking().Where(x => meterIds.Contains(x.MeterId))
                .Select(x => new Row(x.Id, x.MeterId, x.AccumulatedKwh, x.Timestamp, x.Quality, x.RequiresReview)).ToListAsync(ct))
            .Where(x => x.Timestamp < endExclusive).OrderBy(x => x.Timestamp).ThenBy(x => x.Id).ToLookup(x => x.MeterId);
        var tariffs = await db.Tariffs.AsNoTracking().ToListAsync(ct);
        var priorInvoices = await db.Invoices.AsNoTracking().Include(x => x.Payments).ToListAsync(ct);

        foreach (var meter in meters)
        {
            var rows = readings[meter.Id].ToList();
            var usable = rows.Where(x => !x.RequiresReview && x.Quality != RejectedQuality).ToList();
            var openReview = rows.Where(x => x.RequiresReview).FirstOrDefault(review => !Recovered(review, usable));
            if (meter.BacnetCounterReview || openReview is not null)
            {
                var when = openReview is null ? "" : $" on {openReview.Timestamp.ToLocalTime():dd MMM yyyy HH:mm}";
                errors.Add($"{meter.Name}: counter decrease{when} needs review. Open the meter page and choose Ignore reading or Accept new starting value.");
                continue;
            }
            var opening = usable.LastOrDefault(x => x.Timestamp < start);
            var closing = usable.LastOrDefault(x => x.Timestamp >= start);
            var bacnetError = BacnetBillingGuard.Validate(meter, [], closing is null ? null : new MeterReading { Quality = closing.Quality, Timestamp = closing.Timestamp }, endExclusive);
            if (bacnetError is not null) { errors.Add($"{meter.Name}: {bacnetError}"); continue; }
            if (closing is null) { errors.Add($"{meter.Name}: no reading in the period"); continue; }
            if (endExclusive - closing.Timestamp > ClosingStaleWarning && meter.Controller?.CommunicationType != "BacnetIp")
                warnings.Add($"{meter.Name}: the last reading in the period is from {closing.Timestamp.ToLocalTime():dd MMM yyyy HH:mm}; usage after it will be billed next period.");

            // Walk the period readings so a meter reset/replacement (baseline reading) is billed correctly.
            var previous = opening?.Value ?? meter.InitialReading; var consumption = 0m; var resets = new List<DateTimeOffset>(); string? decrease = null;
            foreach (var row in usable.Where(x => x.Timestamp >= start))
            {
                if (row.Quality == BaselineQuality) { previous = row.Value; resets.Add(row.Timestamp); continue; }
                if (row.Value < previous) { decrease = $"reading fell from {previous:N3} to {row.Value:N3} on {row.Timestamp.ToLocalTime():dd MMM yyyy HH:mm} without a review"; break; }
                consumption += row.Value - previous; previous = row.Value;
            }
            if (decrease is not null) { errors.Add($"{meter.Name}: {decrease}"); continue; }

            // Price the period with the tariff in force at its end, never one set after the period.
            Tariff tariff;
            try { tariff = tariffResolver.Resolve(tariffs, meter.Id, endExclusive.AddTicks(-1)); }
            catch (InvalidOperationException)
            {
                try
                {
                    tariff = tariffResolver.Resolve(tariffs, meter.Id, issued);
                    warnings.Add($"{meter.Name}: no tariff was in force during the period; using the tariff set on {tariff.EffectiveFrom.ToLocalTime():dd MMM yyyy}.");
                }
                catch (InvalidOperationException) { errors.Add($"{meter.Name}: no tariff"); continue; }
            }

            // Arrears belong to this meter at this shop; a meter moved to another shop starts clean.
            var previousBalance = AccountBalanceService.Outstanding(priorInvoices.Where(x => x.MeterId == meter.Id && x.ShopId == meter.ShopId));
            BillingResult result;
            try
            {
                result = calculator.Calculate(new(opening?.Value ?? meter.InitialReading, closing.Value, tariff.PricePerKwh,
                    request.FixedCharge, request.OtherCharge, request.TaxPercent, request.Discount, previousBalance, consumption));
            }
            catch (InvalidOperationException ex) { errors.Add($"{meter.Name}: {ex.Message}"); continue; }
            var note = resets.Count == 0 ? "" : $"Meter reset/replaced on {string.Join(", ", resets.Select(x => x.ToLocalTime().ToString("dd MMM yyyy HH:mm")))}; consumption is calculated across the reset.";
            lines.Add(new(meter.Id, meter.Name, meter.Shop?.Name ?? "", meter.Unit, opening?.Value ?? meter.InitialReading, closing.Value,
                opening?.Timestamp, closing.Timestamp, tariff.PricePerKwh, tariff.Currency, previousBalance, result, opening?.Id, closing.Id, note));
        }
        return new(periodStart, periodEnd, lines, errors, warnings, existing?.Id);
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

    /// <summary>Creates the billing period and all its invoices in one transaction, or nothing.</summary>
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
        var usedIds = preview.Lines.SelectMany(x => x.OpeningReadingId is long id ? new[] { id, x.ClosingReadingId } : new[] { x.ClosingReadingId }).ToList();
        foreach (var reading in await db.MeterReadings.Where(x => usedIds.Contains(x.Id)).ToListAsync(ct)) reading.UsedForBilling = true;
        var meters = await db.Meters.AsNoTracking().Where(x => preview.Lines.Select(l => l.MeterId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var next = await NextInvoiceNumberAsync(ct);
        var issued = BillingCycle.LocalStart(request.InvoiceDate); var due = BillingCycle.LocalStart(request.DueDate);
        foreach (var line in preview.Lines)
        {
            db.Invoices.Add(new Invoice
            {
                InvoiceNumber = $"INV-{next++:000000}", BillingPeriodId = period.Id, MeterId = line.MeterId, ShopId = meters[line.MeterId].ShopId,
                InvoiceDate = issued, DueDate = due, OpeningReading = line.Opening, ClosingReading = line.Closing,
                ConsumptionKwh = line.Result.Consumption, TariffPerKwh = line.TariffPerUnit, EnergyCharge = line.Result.EnergyCharge,
                FixedCharges = request.FixedCharge, OtherCharges = request.OtherCharge, Tax = line.Result.Tax, Discount = request.Discount,
                PreviousBalance = line.PreviousBalance, Total = line.Result.Total, Currency = line.Currency,
                Status = request.Publish ? InvoiceStatus.Finalized : InvoiceStatus.Draft, Locked = request.Publish,
                AutoEmailRequested = autoEmail, Notes = line.Note,
                SnapshotJson = JsonSerializer.Serialize(new { PeriodStart = request.PeriodStart, PeriodEnd = request.PeriodEnd, Tariff = line.TariffPerUnit, line.Currency, line.OpeningAt, line.ClosingAt, line.Note })
            });
        }
        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = now, UserId = request.User, Action = request.Publish ? "Invoices published" : "Draft invoices generated",
            EntityType = "BillingPeriod", EntityId = period.Id.ToString(),
            NewValue = $"{preview.Lines.Count} invoices for {request.PeriodStart:yyyy-MM-dd} – {request.PeriodEnd:yyyy-MM-dd}; invoice date {request.InvoiceDate:yyyy-MM-dd}; due {request.DueDate:yyyy-MM-dd}; automatic email={autoEmail}",
            Reason = request.Reason, SourceIp = request.SourceIp
        });
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return new(true, false, $"{preview.Lines.Count} invoice(s) {(request.Publish ? "published" : "created as drafts")} for {request.PeriodStart:dd MMM yyyy} – {request.PeriodEnd:dd MMM yyyy}.", period.Id, preview.Lines.Count);
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
