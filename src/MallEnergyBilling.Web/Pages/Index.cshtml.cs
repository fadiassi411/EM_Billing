using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages;

public class IndexModel(ApplicationDbContext db, PermissionService permissions) : PageModel
{
    public List<Meter> Meters { get; set; } = [];
    public List<Invoice> Invoices { get; set; } = [];
    public List<Tariff> CurrentTariffs { get; set; } = [];
    public int Shops { get; set; }
    public string TariffSummary { get; set; } = "No meter tariffs";
    public string OutstandingSummary { get; set; } = "No unpaid invoices";
    public string CollectionRateSummary { get; set; } = "No invoices";
    public DateTimeOffset? LatestTariffChange { get; set; }

    public bool CanViewInvoices { get; private set; }
    public bool CanManageShops { get; private set; }
    public bool WaterEnabled { get; private set; }

    static readonly string[] AlarmStatuses = ["Failed", "Frozen", "Stale", "Timeout", "BACnet Error", "Configuration Error", "Counter Review"];

    /// <summary>Card colour state: ok (online), bad (alarm), warn (waiting/partial) or off (not in service).</summary>
    public static string Tone(Meter meter)
    {
        var status = meter.DisplayStatus;
        if (!meter.Active || status is "Not commissioned" or "Disabled" or "Archived") return "off";
        if (AlarmStatuses.Contains(status)) return "bad";
        if (status is "Connected" or "Online") return "ok";
        return "warn";
    }

    /// <summary>Meter type shown on the card: Grid, Generator or Water.</summary>
    public static string Kind(Meter meter) => meter.UtilityType == UtilityType.Water ? "Water" : meter.PowerSource.ToString();

    public static string Ago(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is null) return "No reading yet";
        var span = now - at.Value;
        if (span < TimeSpan.FromMinutes(1)) return "Just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} h ago";
        if (span < TimeSpan.FromDays(60)) return $"{(int)span.TotalDays} days ago";
        return at.Value.ToLocalTime().ToString("dd MMM yyyy");
    }

    public async Task<IActionResult> OnGet()
    {
        if (!await permissions.HasAsync(User, Permissions.ViewDashboard))
        {
            // Send the user to the first page their access level allows.
            foreach (var (permission, page) in new[] { (Permissions.ViewMeters, "/PowerSources/Grid"), (Permissions.ViewInvoices, "/Invoices/Index"), (Permissions.ViewOperations, "/Operations/Index"), (Permissions.ManageControllers, "/Admin/Controllers/Index"), (Permissions.ManageShops, "/Admin/Shops/Index"), (Permissions.ViewAudit, "/Admin/Audit/Index") })
                if (await permissions.HasAsync(User, permission)) return RedirectToPage(page);
            return RedirectToPage("/NoAccess");
        }
        CanViewInvoices = await permissions.HasAsync(User, Permissions.ViewInvoices);
        CanManageShops = await permissions.HasAsync(User, Permissions.ManageShops);
        WaterEnabled = await db.SystemFeatureConfigurations.AsNoTracking().AnyAsync(x => x.Id == 1 && x.WaterBillingEnabled);
        var includeWater = WaterEnabled;
        Meters = await db.Meters.Include(x => x.Shop).Include(x => x.Controller)
            .Where(x => (x.UtilityType == UtilityType.Electricity || includeWater) && x.CommunicationStatus != "Archived").ToListAsync();
        var all = (await db.Tariffs.Include(x => x.Meter).ToListAsync()).Where(x => x.MeterId != null && x.Meter?.UtilityType==UtilityType.Electricity && x.EffectiveFrom <= DateTimeOffset.UtcNow);
        CurrentTariffs = all.GroupBy(x => x.MeterId).Select(g => g.OrderByDescending(x => x.EffectiveFrom).First()).ToList();
        if (CurrentTariffs.Count > 0)
        {
            TariffSummary = string.Join(" · ", CurrentTariffs.GroupBy(x => x.Currency).OrderBy(x => x.Key).Select(g =>
            {
                var min = g.Min(x => x.PricePerKwh); var max = g.Max(x => x.PricePerKwh);
                return min == max ? $"{g.Key} {min:0.0000} / kWh" : $"{g.Key} {min:0.0000} - {max:0.0000} / kWh";
            }));
            LatestTariffChange = CurrentTariffs.Max(x => x.CreatedAt);
        }
        Invoices = await db.Invoices.Include(x => x.Shop).Include(x => x.Payments).ToListAsync();
        var balances = Invoices.GroupBy(x => x.Currency).Select(g => new { Currency = g.Key, Balance = AccountBalanceService.Outstanding(g) }).Where(x => x.Balance != 0).OrderBy(x => x.Currency).ToList();
        if (balances.Count > 0) OutstandingSummary = string.Join(" · ", balances.Select(x => $"{x.Currency} {x.Balance:N2}"));
        var rates = Invoices.Where(x => x.Status != InvoiceStatus.Cancelled).GroupBy(x => x.Currency).Select(g => new { Currency = g.Key, Total = g.Sum(AccountBalanceService.Charges), Paid = g.Sum(x => x.PaidAmount) }).Where(x => x.Total != 0).OrderBy(x => x.Currency).ToList();
        if (rates.Count > 0) CollectionRateSummary = string.Join(" · ", rates.Select(x => $"{x.Currency} {(x.Paid / x.Total) * 100:N1}%"));
        Shops = await db.Shops.CountAsync();
        return Page();
    }
}
