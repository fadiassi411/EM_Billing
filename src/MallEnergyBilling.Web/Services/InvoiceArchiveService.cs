using System.IO.Compression;
using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Services;

/// <summary>
/// Keeps a PDF copy of every published invoice on the server, in
/// <c>{data}\Invoices\{year}\{year-month}\INV-000123 - SHOP-03 Bella Zuri - Grid.pdf</c>.
/// The month is the consumption (billing period) month, so the November bill is under 2026-11
/// even though it is published on 1 December. The copy is written once, as issued, and never changed.
/// </summary>
public sealed class InvoiceArchiveService(AppDataPaths paths, InvoicePdfService pdf, ILogger<InvoiceArchiveService> log)
{
    public string RootDirectory => Path.Combine(paths.DataDirectory, "Invoices");

    /// <summary>Invoices that belong in the archive: published, not drafts or cancelled.</summary>
    public static bool IsPublished(Invoice x) => x.Status is not (InvoiceStatus.Draft or InvoiceStatus.Cancelled);

    /// <summary>The month the invoice bills (the billing period start), falling back to the invoice date.</summary>
    public static DateOnly BilledMonth(Invoice x)
    {
        var start = x.BillingPeriod?.StartDate ?? DateOnly.FromDateTime(x.InvoiceDate.ToLocalTime().DateTime);
        return new DateOnly(start.Year, start.Month, 1);
    }

    public static string FileName(Invoice x)
    {
        var utility = x.Meter?.UtilityType == UtilityType.Water ? "Water" : x.Meter?.PowerSource == PowerSource.Generator ? "Generator" : "Grid";
        var name = $"{x.InvoiceNumber} - {x.Shop?.ShopNumber} {x.Shop?.Name} - {x.Meter?.Name} {utility}.pdf";
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '-' : c)).Replace("  ", " ").Trim();
    }

    public string PathFor(Invoice x)
    {
        var month = BilledMonth(x);
        return Path.Combine(RootDirectory, month.Year.ToString("0000"), $"{month:yyyy-MM}", FileName(x));
    }

    /// <summary>Writes the PDF if it is not in the archive yet. Returns true when a file was written.</summary>
    public async Task<bool> EnsureSavedAsync(Invoice invoice, CancellationToken ct = default)
    {
        if (!IsPublished(invoice)) return false;
        var path = PathFor(invoice);
        if (File.Exists(path)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, pdf.Generate(invoice), ct);
        File.Move(temp, path, overwrite: true);
        return true;
    }

    /// <summary>The archived PDF, or a freshly generated one if the file is missing.</summary>
    public async Task<byte[]> ReadAsync(Invoice invoice, CancellationToken ct = default)
    {
        var path = PathFor(invoice);
        if (File.Exists(path)) return await File.ReadAllBytesAsync(path, ct);
        try { await EnsureSavedAsync(invoice, ct); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.LogWarning(ex, "Could not archive invoice {Invoice}", invoice.InvoiceNumber); }
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : pdf.Generate(invoice);
    }

    /// <summary>A ZIP of the given invoices, one folder per billed month.</summary>
    public async Task<byte[]> ZipAsync(IEnumerable<Invoice> invoices, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var invoice in invoices.Where(IsPublished).OrderBy(BilledMonth).ThenBy(x => x.InvoiceNumber))
            {
                var entry = zip.CreateEntry($"{BilledMonth(invoice):yyyy-MM}/{FileName(invoice)}", CompressionLevel.Optimal);
                await using var stream = entry.Open();
                await stream.WriteAsync(await ReadAsync(invoice, ct), ct);
            }
        }
        return buffer.ToArray();
    }
}

/// <summary>Saves every published invoice to the archive folder shortly after it is published, and catches up on start.</summary>
public sealed class InvoiceArchiveBackgroundService(IServiceScopeFactory scopes, InvoiceArchiveService archive, DatabaseMaintenanceService maintenance, ILogger<InvoiceArchiveBackgroundService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), ct);
            while (!ct.IsCancellationRequested)
            {
                try { await ArchivePendingAsync(ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Invoice archive pass failed; retrying"); }
                await Task.Delay(TimeSpan.FromMinutes(2), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    public async Task<int> ArchivePendingAsync(CancellationToken ct = default)
    {
        List<Invoice> invoices;
        await maintenance.Gate.WaitAsync(ct);
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            invoices = (await db.Invoices.AsNoTracking().Include(x => x.Shop).Include(x => x.Meter).Include(x => x.BillingPeriod).Include(x => x.Payments).ToListAsync(ct))
                .Where(InvoiceArchiveService.IsPublished).ToList();
        }
        finally { maintenance.Gate.Release(); }
        var written = 0;
        foreach (var invoice in invoices)
        {
            try { if (await archive.EnsureSavedAsync(invoice, ct)) written++; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.LogWarning(ex, "Could not archive invoice {Invoice}", invoice.InvoiceNumber); }
        }
        if (written > 0) log.LogInformation("Saved {Count} invoice PDF(s) to {Folder}", written, archive.RootDirectory);
        return written;
    }
}
