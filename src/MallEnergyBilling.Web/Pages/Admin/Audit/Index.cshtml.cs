using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages.Admin.Audit;

public sealed class IndexModel(ApplicationDbContext db) : PageModel
{
    public List<AuditLog> Entries { get; private set; } = [];
    public async Task OnGetAsync() => Entries = await db.AuditLogs.OrderByDescending(x => x.Id).Take(500).ToListAsync();
}
