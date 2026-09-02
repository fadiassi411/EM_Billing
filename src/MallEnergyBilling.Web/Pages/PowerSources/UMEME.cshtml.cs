using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;

namespace MallEnergyBilling.Web.Pages.PowerSources;

public sealed class UMEMEModel(ApplicationDbContext db) : PowerSourceMetersPageModel(db, PowerSource.UMEME)
{
    public async Task OnGetAsync() => await LoadMetersAsync();
}
