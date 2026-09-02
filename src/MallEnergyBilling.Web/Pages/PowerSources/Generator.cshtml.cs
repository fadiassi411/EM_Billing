using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;

namespace MallEnergyBilling.Web.Pages.PowerSources;

public sealed class GeneratorModel(ApplicationDbContext db) : PowerSourceMetersPageModel(db, PowerSource.Generator)
{
    public async Task OnGetAsync() => await LoadMetersAsync();
}
