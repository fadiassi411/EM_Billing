namespace MallEnergyBilling.Web.Services;

public sealed class DatabaseMaintenanceService
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
}
