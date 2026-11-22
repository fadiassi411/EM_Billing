using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages.Invoices;

[Authorize]
public sealed class IndexModel(ApplicationDbContext db, InvoiceArchiveService archive) : PageModel
{
    public List<Invoice> Invoices { get; private set; } = [];
    public List<Shop> Shops { get; private set; } = [];
    public List<int> Years { get; private set; } = [];
    public string Search { get; private set; } = "";
    public int? ShopId { get; private set; }
    public int? Year { get; private set; }
    public int? Month { get; private set; }
    public string ArchiveFolder => archive.RootDirectory;
    public bool Filtered => Search.Length > 0 || ShopId is not null || Year is not null || Month is not null;

    public async Task OnGetAsync(string? q, int? shop, int? year, int? month)
    {
        await LoadAsync(q, shop, year, month);
    }

    /// <summary>Downloads one invoice PDF (the archived copy as issued).</summary>
    public async Task<IActionResult> OnGetPdfAsync(int id)
    {
        var invoice = await Query().FirstOrDefaultAsync(x => x.Id == id);
        if (invoice is null || !InvoiceArchiveService.IsPublished(invoice)) return NotFound();
        return File(await archive.ReadAsync(invoice, HttpContext.RequestAborted), "application/pdf", InvoiceArchiveService.FileName(invoice));
    }

    /// <summary>Downloads every invoice that matches the current filters as one ZIP file.</summary>
    public async Task<IActionResult> OnGetZipAsync(string? q, int? shop, int? year, int? month)
    {
        await LoadAsync(q, shop, year, month);
        if (Invoices.Count == 0) { TempData["InvoiceError"] = "No invoices match these filters, so there is nothing to download."; return RedirectToPage(new { q, shop, year, month }); }
        var name = "Invoices";
        if (ShopId is int id && Shops.FirstOrDefault(x => x.Id == id) is { } s) name += $" - {s.ShopNumber} {s.Name}";
        if (Year is int y) name += Month is int m ? $" - {y:0000}-{m:00}" : $" - {y:0000}";
        var invalid = Path.GetInvalidFileNameChars();
        name = string.Concat(name.Select(c => invalid.Contains(c) ? '-' : c));
        return File(await archive.ZipAsync(Invoices, HttpContext.RequestAborted), "application/zip", name + ".zip");
    }

    IQueryable<Invoice> Query() => db.Invoices.AsNoTracking().Include(x => x.Shop).Include(x => x.Meter).Include(x => x.BillingPeriod).Include(x => x.Payments);

    async Task LoadAsync(string? q, int? shop, int? year, int? month)
    {
        Search = (q ?? "").Trim();
        ShopId = shop is > 0 ? shop : null;
        Month = month is >= 1 and <= 12 ? month : null;
        Year = year is >= 2000 and <= 2100 ? year : null;
        var all = (await Query().ToListAsync()).Where(InvoiceArchiveService.IsPublished).ToList();
        Shops = all.Where(x => x.Shop is not null).Select(x => x.Shop!).DistinctBy(x => x.Id).OrderBy(x => x.ShopNumber).ToList();
        Years = all.Select(x => InvoiceArchiveService.BilledMonth(x).Year).Distinct().OrderByDescending(x => x).ToList();
        var invoices = all.AsEnumerable();
        if (ShopId is int id) invoices = invoices.Where(x => x.ShopId == id);
        if (Year is int y) invoices = invoices.Where(x => InvoiceArchiveService.BilledMonth(x).Year == y);
        if (Month is int m) invoices = invoices.Where(x => InvoiceArchiveService.BilledMonth(x).Month == m);
        if (Search.Length > 0) { var term = Search.ToLowerInvariant(); invoices = invoices.Where(x => Matches(x, term)); }
        Invoices = invoices.OrderByDescending(x => InvoiceArchiveService.BilledMonth(x)).ThenByDescending(x => x.Id).ToList();
    }

    static bool Matches(Invoice x, string term)
    {
        var local = x.InvoiceDate.ToLocalTime();
        var billed = InvoiceArchiveService.BilledMonth(x);
        var searchable = $"{x.InvoiceNumber} {x.Shop?.Name} {x.Shop?.ShopNumber} {x.Shop?.TenantName} {x.Meter?.Name} {x.Meter?.PowerSource} {local:dd} {local:MM} {local:yyyy} {local:dd/MM/yyyy} {local:dd MMM yyyy} {local:MMMM yyyy} {billed:MM-yyyy} {billed:yyyy-MM} {billed:MMMM yyyy}".ToLowerInvariant();
        return searchable.Contains(term);
    }
}
