using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages.PowerSources;

public abstract class PowerSourceMetersPageModel(ApplicationDbContext db, PowerSource source) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "q")]
    public string Search { get; set; } = "";

    public IReadOnlyList<Meter> Meters { get; private set; } = [];
    public PowerSource Source { get; } = source;
    public string Title => Source == PowerSource.Grid ? "Grid" : "Generator";
    public string Description => Source == PowerSource.Grid
        ? "Electricity meters supplied by the public utility grid."
        : "Electricity meters supplied by on-site generators.";
    public string PagePath => Source == PowerSource.Grid ? "/PowerSources/Grid" : "/PowerSources/Generator";

    protected async Task LoadMetersAsync()
    {
        var meters = await db.Meters
            .AsNoTracking()
            .Include(x => x.Shop)
            .Include(x => x.Controller)
            .Where(x => x.PowerSource == Source)
            .OrderBy(x => x.Name)
            .ToListAsync();

        var term = Search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            meters = meters.Where(x => MeterMatchesSearch(x, term)).ToList();

        Search = term ?? "";
        Meters = meters;
    }

    public static bool MeterMatchesSearch(Meter meter, string term)
    {
        var values = new[]
        {
            meter.Id.ToString(), meter.SerialNumber, meter.Name, meter.Model,
            meter.Shop?.ShopNumber, meter.Shop?.Name, meter.Shop?.TenantName,
            meter.Shop?.Floor, meter.Shop?.Zone, meter.Shop?.MdbPanel,
            meter.Shop?.BillingAddress, meter.Controller?.Name, meter.Controller?.MdbPanel
        };
        return values.Any(x => !string.IsNullOrWhiteSpace(x) && x.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
