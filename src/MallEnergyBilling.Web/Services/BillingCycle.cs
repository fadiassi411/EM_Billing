namespace MallEnergyBilling.Web.Services;

/// <summary>
/// Monthly billing cycle rules. A cycle is defined by one start day (1–28); the period
/// ends the day before the next start day, so periods can never overlap or leave gaps.
/// Invoices are published a fixed number of days after the period ends.
/// </summary>
public static class BillingCycle
{
    public const int MinStartDay = 1, MaxStartDay = 28, MaxPublishDelayDays = 7;
    public static readonly TimeOnly RunTime = new(0, 5);

    public static bool IsValidStartDay(int day) => day is >= MinStartDay and <= MaxStartDay;
    public static bool IsValidPublishDelay(int days) => days is >= 0 and <= MaxPublishDelayDays;

    /// <summary>The most recent cycle start on or before <paramref name="date"/>.</summary>
    public static DateOnly LatestCycleStart(DateOnly date, int startDay)
    {
        var candidate = new DateOnly(date.Year, date.Month, startDay);
        return candidate <= date ? candidate : candidate.AddMonths(-1);
    }

    /// <summary>The complete period that ends the day before <paramref name="cycleStart"/>.</summary>
    public static (DateOnly Start, DateOnly End) PeriodEndingBefore(DateOnly cycleStart) =>
        (cycleStart.AddMonths(-1), cycleStart.AddDays(-1));

    /// <summary>The last fully completed period as of <paramref name="date"/>.</summary>
    public static (DateOnly Start, DateOnly End) LastCompletedPeriod(DateOnly date, int startDay) =>
        PeriodEndingBefore(LatestCycleStart(date, startDay));

    /// <summary>First publication date on or after <paramref name="today"/>.</summary>
    public static DateOnly FirstRunOnOrAfter(DateOnly today, int startDay, int publishDelayDays)
    {
        var run = LatestCycleStart(today, startDay).AddDays(publishDelayDays);
        return run >= today ? run : LatestCycleStart(today, startDay).AddMonths(1).AddDays(publishDelayDays);
    }

    /// <summary>The cycle start (= day after period end) that a publication date belongs to.</summary>
    public static DateOnly CycleStartForRun(DateOnly runDate, int publishDelayDays) => runDate.AddDays(-publishDelayDays);

    public static DateTimeOffset LocalTime(DateOnly date, TimeOnly time, TimeZoneInfo? zone = null)
    {
        var value = date.ToDateTime(time);
        return new DateTimeOffset(value, (zone ?? TimeZoneInfo.Local).GetUtcOffset(value));
    }

    public static DateTimeOffset LocalStart(DateOnly date, TimeZoneInfo? zone = null) => LocalTime(date, TimeOnly.MinValue, zone);

    public static string Describe(int startDay) => startDay == 1
        ? "1st to the last day of each month"
        : $"{Ordinal(startDay)} to the {Ordinal(startDay - 1)} of the next month";

    public static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
}
