using MallEnergyBilling.Web.Models;

namespace MallEnergyBilling.Web.Services;

public static class BacnetBillingGuard
{
    // Keep established consumption/tariff calculations. Refuse a BACnet period whose
    // endpoint is stale or whose counter needs review, instead of inventing consumption.
    public static string? Validate(Meter meter, IReadOnlyList<MeterReading> rows, MeterReading? closing, DateTimeOffset endExclusive)
    {
        if (meter.Controller?.CommunicationType != "BacnetIp") return null;
        if (meter.BacnetCounterReview || rows.Any(x => x.RequiresReview)) return "BACnet counter/reset requires review before billing.";
        if (closing is null || closing.Quality != "Good" || endExclusive - closing.Timestamp > TimeSpan.FromMinutes(16).Add(TimeSpan.FromSeconds(meter.Controller.PollingIntervalSeconds)))
            return "No fresh, good cumulative BACnet energy reading near the end of the billing period.";
        return null;
    }
}
