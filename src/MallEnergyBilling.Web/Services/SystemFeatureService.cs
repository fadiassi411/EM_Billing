using MallEnergyBilling.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Services;

public sealed class SystemFeatureService(ApplicationDbContext db)
{
    public async Task<bool> WaterBillingEnabledAsync(CancellationToken token = default) =>
        await db.SystemFeatureConfigurations.AsNoTracking().AnyAsync(x => x.Id == 1 && x.WaterBillingEnabled, token);

    /// <summary>Draft invoices waiting for a billing user to approve them.</summary>
    public async Task<int> DraftInvoiceCountAsync(CancellationToken token = default) =>
        await db.Invoices.AsNoTracking().CountAsync(x => x.Status == MallEnergyBilling.Web.Models.InvoiceStatus.Draft, token);
}
