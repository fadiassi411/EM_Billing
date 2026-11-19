namespace MallEnergyBilling.Web.Services;

public sealed class DatabaseMaintenanceService
{
    private int generation;

    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>Changes every time the database file is replaced (restore). Pollers compare it before saving
    /// so results read from the old database are never written into a freshly restored one.</summary>
    public int Generation => Volatile.Read(ref generation);

    public void DatabaseReplaced() => Interlocked.Increment(ref generation);
}
