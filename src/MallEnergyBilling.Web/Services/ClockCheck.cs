using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Services;

/// <summary>
/// Finds readings stamped after the current time. They appear when the PC clock was moved forward
/// (for example to test billing) and back again. Left alone they stop the reading history and later
/// look like a counter decrease, so an administrator can reject them (they are kept, never billed).
/// </summary>
public static class ClockCheck
{
    public const string RejectReason = "Dated after the current time: the PC clock was moved forward and back.";
    static readonly TimeSpan Margin = TimeSpan.FromMinutes(10);

    public static async Task<List<MeterReading>> FutureReadingsAsync(ApplicationDbContext db, DateTimeOffset now, CancellationToken ct = default)
    {
        // SQLite stores the timestamps as text, so pre-filter in SQL with a one-day margin that covers any
        // time-zone offset, then check exactly here.
        var cut = now.UtcDateTime.AddDays(-1).ToString("yyyy-MM-dd HH:mm:ss");
        var ids = await db.Database.SqlQuery<long>($"SELECT Id AS Value FROM MeterReadings WHERE Timestamp > {cut} AND Quality <> {BillingRunService.RejectedQuality}").ToListAsync(ct);
        if (ids.Count == 0) return [];
        var rows = new List<MeterReading>();
        foreach (var chunk in ids.Chunk(500))
            rows.AddRange(await db.MeterReadings.Include(x => x.Meter).Where(x => chunk.Contains(x.Id)).ToListAsync(ct));
        return rows.Where(x => x.Timestamp > now + Margin).OrderBy(x => x.Timestamp).ToList();
    }

    /// <summary>Marks the future-dated readings as Rejected. Returns how many were rejected.</summary>
    public static async Task<int> RejectFutureReadingsAsync(ApplicationDbContext db, DateTimeOffset now, string user, string sourceIp, CancellationToken ct = default)
    {
        var rows = await FutureReadingsAsync(db, now, ct);
        if (rows.Count == 0) return 0;
        foreach (var row in rows) { row.Quality = BillingRunService.RejectedQuality; row.RequiresReview = false; row.Reason = RejectReason; }
        // A meter whose last reading time is in the future would look stale or odd on the dashboard.
        foreach (var meter in rows.Select(x => x.Meter).OfType<Meter>().Distinct())
            if (meter.LastReadingAt > now + Margin) meter.LastReadingAt = now;
        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = now, UserId = user, Action = "Future-dated readings rejected", EntityType = "MeterReading",
            EntityId = string.Join(",", rows.Select(x => x.Id).Take(50)),
            NewValue = $"{rows.Count} reading(s) from {rows[0].Timestamp:yyyy-MM-dd HH:mm} to {rows[^1].Timestamp:yyyy-MM-dd HH:mm} marked Rejected",
            Reason = RejectReason, SourceIp = sourceIp
        });
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }
}
