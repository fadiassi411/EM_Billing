using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages.Meters;

/// <summary>
/// Resolves suspect counter readings. Until a decrease is resolved the meter is not billed,
/// so the administrator decides whether it was a glitch or a real meter reset/replacement.
/// </summary>
[Authorize(Policy = "AdministratorOnly")]
public sealed class ReviewModel(ApplicationDbContext db, TimeProvider time) : PageModel
{
    public Meter Meter { get; private set; } = null!;
    public List<MeterReading> PendingReviews { get; private set; } = [];
    public List<MeterReading> RecentReadings { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id) => await Load(id) ? Page() : NotFound();

    /// <summary>The low value was a communication glitch: exclude it from billing.</summary>
    public async Task<IActionResult> OnPostIgnoreAsync(int id, long readingId, string reason)
    {
        if (!await Load(id)) return NotFound();
        var reading = PendingReviews.FirstOrDefault(x => x.Id == readingId);
        if (reading is null) return Message("That reading no longer needs review.", false, id);
        if (string.IsNullOrWhiteSpace(reason)) return Message("Enter a reason for the audit trail.", false, id);
        reading.RequiresReview = false; reading.Quality = BillingRunService.RejectedQuality; reading.Reason = reason.Trim();
        // The counter may still be low; the next poll will raise a new review if so.
        Meter.BacnetCounterReview = false;
        if (Meter.CommunicationStatus == "Counter Review") Meter.CommunicationStatus = "Awaiting poll";
        Audit("Counter reading ignored", $"{reading.AccumulatedKwh} at {reading.Timestamp:O}", "Excluded from billing", reason);
        await db.SaveChangesAsync();
        return Message($"Reading {reading.AccumulatedKwh:N3} was excluded from billing.", true, id);
    }

    /// <summary>The meter was really reset or replaced: the low value is the new counter's starting value.</summary>
    public async Task<IActionResult> OnPostAcceptAsync(int id, long readingId, string reason)
    {
        if (!await Load(id)) return NotFound();
        var reading = PendingReviews.FirstOrDefault(x => x.Id == readingId);
        if (reading is null) return Message("That reading no longer needs review.", false, id);
        if (string.IsNullOrWhiteSpace(reason)) return Message("Enter a reason for the audit trail.", false, id);
        reading.RequiresReview = false; reading.Quality = BillingRunService.BaselineQuality; reading.Reason = reason.Trim();
        // Earlier pending reviews belong to the same reset and are superseded by this starting value.
        foreach (var older in PendingReviews.Where(x => x.Timestamp < reading.Timestamp))
        { older.RequiresReview = false; older.Quality = BillingRunService.RejectedQuality; older.Reason = "Superseded by new starting value: " + reason.Trim(); }
        Meter.BacnetCounterReview = false; Meter.LastReading = reading.AccumulatedKwh; Meter.LastReadingAt = reading.Timestamp;
        Meter.CommunicationStatus = "Awaiting poll";
        Audit("Counter reset accepted", $"last accepted {RecentReadings.FirstOrDefault(x => !x.RequiresReview && x.Id != reading.Id)?.AccumulatedKwh}", $"new starting value {reading.AccumulatedKwh} at {reading.Timestamp:O}", reason);
        await db.SaveChangesAsync();
        return Message($"New starting value {reading.AccumulatedKwh:N3} accepted. Billing continues from it.", true, id);
    }

    /// <summary>Before or after physically replacing a meter: the next value read starts a new counter.</summary>
    public async Task<IActionResult> OnPostReplacedAsync(int id, string reason)
    {
        if (!await Load(id)) return NotFound();
        if (string.IsNullOrWhiteSpace(reason)) return Message("Enter a reason for the audit trail.", false, id);
        Meter.RebaseOnNextReading = true;
        Audit("Meter marked as replaced", $"last reading {Meter.LastReading}", "Next reading starts a new counter", reason);
        await db.SaveChangesAsync();
        return Message("The next reading will be stored as the new meter's starting value. Resolve any pending reviews above as well.", true, id);
    }

    async Task<bool> Load(int id)
    {
        var meter = await db.Meters.Include(x => x.Shop).Include(x => x.Controller).FirstOrDefaultAsync(x => x.Id == id);
        if (meter is null) return false;
        Meter = meter;
        var readings = (await db.MeterReadings.Where(x => x.MeterId == id).OrderByDescending(x => x.Id).Take(500).ToListAsync()).OrderByDescending(x => x.Timestamp).ToList();
        PendingReviews = readings.Where(x => x.RequiresReview).ToList();
        RecentReadings = readings.Take(20).ToList();
        return true;
    }

    void Audit(string action, string oldValue, string newValue, string reason) => db.AuditLogs.Add(new AuditLog
    {
        Timestamp = time.GetUtcNow(), UserId = User.Identity?.Name ?? "Administrator", Action = action, EntityType = "Meter", EntityId = Meter.Id.ToString(),
        OldValue = oldValue, NewValue = newValue, Reason = reason.Trim(), SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
    });

    IActionResult Message(string text, bool success, int id) { TempData[success ? "ReviewSuccess" : "ReviewError"] = text; return RedirectToPage(new { id }); }
}
