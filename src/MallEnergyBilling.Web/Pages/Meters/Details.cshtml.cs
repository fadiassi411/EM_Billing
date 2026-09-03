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
        var usage = new List<UsagePoint>();
        for (var i = 1; i < readings.Count; i++)
        {
            var amount = readings[i].AccumulatedKwh - readings[i - 1].AccumulatedKwh;
            if (amount >= 0) usage.Add(new UsagePoint(readings[i].Timestamp.ToLocalTime(), amount));
        }

        var now = DateTimeOffset.Now;
        DayChart = Build("Per Day", Enumerable.Range(0, 24).Select(hour =>
            new ChartBar($"{hour:00}:00", usage.Where(x => x.At.Date == now.Date && x.At.Hour == hour).Sum(x => x.Kwh))).ToList(), "No readings today");
        var days = DateTime.DaysInMonth(now.Year, now.Month);
        MonthChart = Build("Per Month", Enumerable.Range(1, days).Select(day =>
            new ChartBar(day.ToString("00"), usage.Where(x => x.At.Year == now.Year && x.At.Month == now.Month && x.At.Day == day).Sum(x => x.Kwh))).ToList(), "No readings this month");
        MonthChart = MonthChart with { AxisStep = 5 };
        YearChart = Build("Per Year", Enumerable.Range(1, 12).Select(month =>
            new ChartBar(new DateTime(now.Year, month, 1).ToString("MMM"), usage.Where(x => x.At.Year == now.Year && x.At.Month == month).Sum(x => x.Kwh))).ToList(), "No readings this year");
        return Page();
    }

    static UsageChart Build(string title, List<ChartBar> bars, string emptyMessage)
    {
        var total = bars.Sum(x => x.Value);
        var max = bars.Count == 0 ? 0 : bars.Max(x => x.Value);
        return new(title, bars, total, max, emptyMessage, 1);
    }

    sealed record UsagePoint(DateTimeOffset At, decimal Kwh);
    public sealed record ChartBar(string Label, decimal Value);
    public sealed record UsageChart(string Title, IReadOnlyList<ChartBar> Bars, decimal Total, decimal Max, string EmptyMessage, int AxisStep)
    {
        public static UsageChart Empty(string title, string message) => new(title, [], 0, 0, message, 1);
        public decimal Height(decimal value) => Max <= 0 ? 0 : Math.Max(2, decimal.Round(value / Max * 100, 2));
    }
}
