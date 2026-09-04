using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages.Invoices;

[Authorize]
public sealed class IndexModel(ApplicationDbContext db) : PageModel
{
    public List<Invoice> Invoices { get; private set; } = [];
    public string Search { get; private set; } = "";

    public async Task OnGetAsync(string? q)
    {
        Search = (q ?? "").Trim();
        var invoices = await db.Invoices.Include(x => x.Shop).Include(x => x.Meter).Include(x => x.Payments)
            .Where(x => x.Status != InvoiceStatus.Draft && x.Status != InvoiceStatus.Cancelled).ToListAsync();
        if (Search.Length > 0)
        {
            var term = Search.ToLowerInvariant();
            invoices = invoices.Where(x => Matches(x, term)).ToList();
        }
        Invoices = invoices.OrderByDescending(x => x.InvoiceDate).ThenByDescending(x => x.Id).ToList();
    }

    static bool Matches(Invoice x, string term)
    {
        var local = x.InvoiceDate.ToLocalTime();
        var searchable = $"{x.InvoiceNumber} {x.Shop?.Name} {x.Shop?.ShopNumber} {x.Shop?.TenantName} {x.Meter?.Name} {x.Meter?.PowerSource} {local:dd} {local:MM} {local:yyyy} {local:dd/MM/yyyy} {local:dd MMM yyyy} {local:MMMM yyyy}".ToLowerInvariant();
        return searchable.Contains(term);
    }
}
