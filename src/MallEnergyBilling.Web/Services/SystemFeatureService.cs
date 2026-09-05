using MallEnergyBilling.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Services;

public sealed class SystemFeatureService(ApplicationDbContext db)
{
    public async Task<bool> WaterBillingEnabledAsync(CancellationToken token = default) =>
        await db.SystemFeatureConfigurations.AsNoTracking().AnyAsync(x => x.Id == 1 && x.WaterBillingEnabled, token);
}
