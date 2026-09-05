using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages.Meters;

public sealed class DetailsModel(ApplicationDbContext db) : PageModel
{
    public Meter Meter { get; private set; } = null!;
    public UsageChart DayChart { get; private set; } = UsageChart.Empty("Per Day", "No readings today");
    public UsageChart MonthChart { get; private set; } = UsageChart.Empty("Per Month", "No readings this month");
    public UsageChart YearChart { get; private set; } = UsageChart.Empty("Per Year", "No readings this year");

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Meter = await db.Meters.Include(x => x.Shop).Include(x => x.Controller).FirstOrDefaultAsync(x => x.Id == id) ?? null!;
        if (Meter is null) return NotFound();

        var readings = (await db.MeterReadings.Where(x => x.MeterId == id && !x.RequiresReview).ToListAsync())
            .OrderBy(x => x.Timestamp).ToList();
        var tariffs = (await db.Tariffs.Where(x => x.MeterId == id).ToListAsync())
            .OrderBy(x => x.EffectiveFrom).ThenBy(x => x.Id).ToList();
        var currentTariff = tariffs.LastOrDefault();
        var usage = new List<UsagePoint>();
        for (var i = 1; i < readings.Count; i++)
        {
            var amount = readings[i].AccumulatedKwh - readings[i - 1].AccumulatedKwh;
            if (amount >= 0)
            {
                var tariff = tariffs.LastOrDefault(x => x.EffectiveFrom <= readings[i].Timestamp) ?? currentTariff;
                usage.Add(new UsagePoint(readings[i].Timestamp.ToLocalTime(), amount,
                    tariff is null ? null : amount * tariff.PricePerKwh, tariff?.Currency));
            }
        }

        var now = DateTimeOffset.Now;
        DayChart = Build("Per Day", Enumerable.Range(0, 24).Select(hour =>
            CreateBar($"{hour:00}:00", $"Hour {hour:00}:00–{hour:00}:59", usage.Where(x => x.At.Date == now.Date && x.At.Hour == hour))).ToList(), "No readings today");
        var days = DateTime.DaysInMonth(now.Year, now.Month);
        MonthChart = Build("Per Month", Enumerable.Range(1, days).Select(day =>
            CreateBar(day.ToString("00"), $"{new DateTime(now.Year, now.Month, day):dd MMM yyyy}", usage.Where(x => x.At.Year == now.Year && x.At.Month == now.Month && x.At.Day == day))).ToList(), "No readings this month");
        MonthChart = MonthChart with { AxisStep = 5 };
        YearChart = Build("Per Year", Enumerable.Range(1, 12).Select(month =>
            CreateBar(new DateTime(now.Year, month, 1).ToString("MMM"), $"{new DateTime(now.Year, month, 1):MMMM yyyy}", usage.Where(x => x.At.Year == now.Year && x.At.Month == month))).ToList(), "No readings this year");
        return Page();
    }

    static ChartBar CreateBar(string label, string period, IEnumerable<UsagePoint> source)
    {
        var points = source.ToList();
        var value = points.Sum(x => x.Kwh);
        var costs = points.Where(x => x.Cost.HasValue && !string.IsNullOrWhiteSpace(x.Currency))
            .GroupBy(x => x.Currency!)
            .Select(x => $"{x.Key} {x.Sum(y => y.Cost!.Value):N2}")
            .ToList();
        var costText = costs.Count > 0 ? string.Join(" + ", costs) : value > 0 ? "Cost unavailable (no active tariff)" : "Cost 0.00";
        return new(label, value, $"{period} · {value:N3} kWh · {costText}");
    }

    static UsageChart Build(string title, List<ChartBar> bars, string emptyMessage)
    {
        var total = bars.Sum(x => x.Value);
        var max = bars.Count == 0 ? 0 : bars.Max(x => x.Value);
        return new(title, bars, total, max, emptyMessage, 1);
    }

    sealed record UsagePoint(DateTimeOffset At, decimal Kwh, decimal? Cost, string? Currency);
    public sealed record ChartBar(string Label, decimal Value, string Tooltip);
    public sealed record UsageChart(string Title, IReadOnlyList<ChartBar> Bars, decimal Total, decimal Max, string EmptyMessage, int AxisStep)
    {
        public static UsageChart Empty(string title, string message) => new(title, [], 0, 0, message, 1);
        public decimal Height(decimal value) => Max <= 0 ? 0 : Math.Max(2, decimal.Round(value / Max * 100, 2));
    }
}
