using System.IO.Compression;
using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MallEnergyBilling.Tests;

public sealed class InvoiceArchiveTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "watchdog-archive-" + Guid.NewGuid().ToString("N"));
    readonly InvoiceArchiveService archive;

    public InvoiceArchiveTests() => archive = new InvoiceArchiveService(new AppDataPaths(dir), new InvoicePdfService(), NullLogger<InvoiceArchiveService>.Instance);
    public void Dispose() { if (Directory.Exists(dir)) Directory.Delete(dir, true); }

    static Invoice Invoice(int id, string shopNumber, string shopName, PowerSource source, DateOnly periodStart, InvoiceStatus status = InvoiceStatus.Finalized) => new()
    {
        Id = id, InvoiceNumber = $"INV-{id:000000}", Status = status, Currency = "UGX", Total = 100, ConsumptionKwh = 10, TariffPerKwh = 10, EnergyCharge = 100,
        InvoiceDate = new DateTimeOffset(periodStart.AddMonths(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(2)),
        DueDate = new DateTimeOffset(periodStart.AddMonths(1).AddDays(10).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(2)),
        Shop = new Shop { ShopNumber = shopNumber, Name = shopName, TenantName = shopName },
        Meter = new Meter { Name = shopName, SerialNumber = "SN-" + id, PowerSource = source },
        BillingPeriod = new BillingPeriod { StartDate = periodStart, EndDate = periodStart.AddMonths(1).AddDays(-1) }
    };

    [Fact] public void TheNovemberBillIsFiledUnderNovemberEvenThoughPublishedInDecember()
    {
        var invoice = Invoice(4, "SHOP-03", "Bella Zuri", PowerSource.Grid, new DateOnly(2026, 11, 1));
        Assert.Equal(new DateOnly(2026, 11, 1), InvoiceArchiveService.BilledMonth(invoice));
        Assert.Equal(Path.Combine(dir, "Invoices", "2026", "2026-11", "INV-000004 - SHOP-03 Bella Zuri - Bella Zuri Grid.pdf"), archive.PathFor(invoice));
    }

    [Fact] public void FileNamesAreSafe()
    {
        var invoice = Invoice(5, "SHOP/05", "A:B*C", PowerSource.Generator, new DateOnly(2026, 11, 1));
        Assert.DoesNotContain(InvoiceArchiveService.FileName(invoice), c => Path.GetInvalidFileNameChars().Contains(c));
    }

    [Fact] public async Task PublishedInvoiceIsSavedOnceAsIssuedAndDraftsAreNotSaved()
    {
        var invoice = Invoice(6, "SHOP-03", "Bella Zuri", PowerSource.Grid, new DateOnly(2026, 11, 1));
        Assert.True(await archive.EnsureSavedAsync(invoice));
        var path = archive.PathFor(invoice);
        Assert.True(File.Exists(path));
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4));
        var written = File.GetLastWriteTimeUtc(path);
        invoice.Status = InvoiceStatus.Paid; // a later payment does not rewrite the issued copy
        Assert.False(await archive.EnsureSavedAsync(invoice));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));

        var draft = Invoice(7, "SHOP-03", "Bella Zuri", PowerSource.Grid, new DateOnly(2026, 11, 1), InvoiceStatus.Draft);
        Assert.False(await archive.EnsureSavedAsync(draft));
        Assert.False(File.Exists(archive.PathFor(draft)));
    }

    [Fact] public async Task AYearOfInvoicesDownloadsAsOneZipWithAFolderPerMonth()
    {
        var invoices = Enumerable.Range(1, 12).Select(m => Invoice(100 + m, "SHOP-03", "Bella Zuri", PowerSource.Grid, new DateOnly(2026, m, 1))).ToList();
        invoices.Add(Invoice(200, "SHOP-03", "Bella Zuri", PowerSource.Grid, new DateOnly(2026, 5, 1), InvoiceStatus.Cancelled)); // never included
        using var zip = new ZipArchive(new MemoryStream(await archive.ZipAsync(invoices)));
        Assert.Equal(12, zip.Entries.Count);
        Assert.Contains(zip.Entries, e => e.FullName == "2026-11/INV-000111 - SHOP-03 Bella Zuri - Bella Zuri Grid.pdf");
        Assert.All(zip.Entries, e => Assert.True(e.Length > 1000));
        // Downloading also filled the server folder.
        Assert.Equal(12, Directory.GetFiles(Path.Combine(dir, "Invoices", "2026"), "*.pdf", SearchOption.AllDirectories).Length);
    }
}
