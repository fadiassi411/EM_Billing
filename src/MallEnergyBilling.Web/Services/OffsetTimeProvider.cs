namespace MallEnergyBilling.Web.Services;

/// <summary>
/// Test-only clock that runs ahead of (or behind) the real clock by a fixed offset.
/// Enabled only in the Development environment through the "Testing:ClockOffsetDays" setting,
/// so a test installation can see a month-end invoice run without waiting for it.
/// </summary>
public sealed class OffsetTimeProvider(TimeSpan offset) : TimeProvider
{
    public TimeSpan Offset { get; } = offset;
    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + Offset;
}
