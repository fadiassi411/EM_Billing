using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;

namespace MallEnergyBilling.Web.Pages.PowerSources;

public sealed class GridModel(ApplicationDbContext db) : PowerSourceMetersPageModel(db, PowerSource.Grid)
{
    public async Task OnGetAsync() => await LoadMetersAsync();
}
