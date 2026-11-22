using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages.Admin.Shops;

public sealed class IndexModel(ApplicationDbContext db) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public List<Shop> Shops { get; private set; } = [];
    public Dictionary<int, int> MeterCounts { get; private set; } = [];
    public Dictionary<int, int> InvoiceCounts { get; private set; } = [];

    /// <summary>Why a shop cannot be deleted, or null when it can.</summary>
    public string? DeleteBlockedReason(int shopId)
    {
        var meters = MeterCounts.GetValueOrDefault(shopId);
        var invoices = InvoiceCounts.GetValueOrDefault(shopId);
        if (meters > 0) return $"This shop still has {meters} meter{(meters == 1 ? "" : "s")}. Move them to another shop or delete them in Controller setup first.";
        if (invoices > 0) return $"This shop has {invoices} invoice{(invoices == 1 ? "" : "s")} in its billing history, which must be kept. Set its status to Inactive or Vacant instead.";
        return null;
    }

    public sealed class InputModel
    {
        [Required, StringLength(40)] public string ShopNumber { get; set; } = "";
        [Required, StringLength(100)] public string Name { get; set; } = "";
        [StringLength(100)] public string TenantName { get; set; } = "";
        [StringLength(40)] public string ContactPerson { get; set; } = "";
        [StringLength(30)] public string Telephone { get; set; } = "";
        [EmailAddress, StringLength(100)] public string Email { get; set; } = "";
        [StringLength(30)] public string Floor { get; set; } = "";
        [StringLength(30)] public string Zone { get; set; } = "";
        [StringLength(40)] public string MdbPanel { get; set; } = "";
        public ShopStatus Status { get; set; } = ShopStatus.Active;
        [StringLength(200)] public string BillingAddress { get; set; } = "";
        [StringLength(40)] public string TaxNumber { get; set; } = "";
        [StringLength(500)] public string Notes { get; set; } = "";
        [StringLength(300)] public string Reason { get; set; } = "";
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        Input.ShopNumber = (Input.ShopNumber ?? "").Trim().ToUpperInvariant();
        if (await db.Shops.AnyAsync(x => x.ShopNumber == Input.ShopNumber))
            ModelState.AddModelError("Input.ShopNumber", "This shop number already exists.");
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }

        var shop = new Shop
        {
            ShopNumber=Input.ShopNumber, Name=Input.Name.Trim(), TenantName=Input.TenantName?.Trim()??"",
            ContactPerson=Input.ContactPerson?.Trim()??"", Telephone=Input.Telephone?.Trim()??"", Email=Input.Email?.Trim()??"",
            Floor=Input.Floor?.Trim()??"", Zone=Input.Zone?.Trim()??"", MdbPanel=Input.MdbPanel?.Trim()??"", Status=Input.Status,
            BillingAddress=Input.BillingAddress?.Trim()??"", TaxNumber=Input.TaxNumber?.Trim()??"", Notes=Input.Notes?.Trim()??""
        };
        db.Shops.Add(shop);
        await db.SaveChangesAsync();
        db.AuditLogs.Add(new AuditLog { Timestamp=DateTimeOffset.UtcNow, UserId=User.Identity?.Name??"Administrator", Action="Shop created", EntityType="Shop", EntityId=shop.Id.ToString(), NewValue=JsonSerializer.Serialize(shop), Reason=string.IsNullOrWhiteSpace(Input.Reason)?"Not provided":Input.Reason.Trim(), SourceIp=HttpContext.Connection.RemoteIpAddress?.ToString()??"" });
        await db.SaveChangesAsync();
        TempData["Message"] = $"Shop {shop.ShopNumber} added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int shopId)
    {
        var shop = await db.Shops.FirstOrDefaultAsync(x => x.Id == shopId);
        if (shop is null)
        {
            TempData["Error"] = "That shop no longer exists. It was probably already deleted; the list below is up to date.";
            return RedirectToPage();
        }
        await LoadCountsAsync();
        // Meters and invoices point at the shop; deleting it would cascade and erase them, so refuse.
        if (DeleteBlockedReason(shop.Id) is { } reason)
        {
            TempData["Error"] = $"{shop.ShopNumber} was not deleted. {reason}";
            return RedirectToPage();
        }
        db.Shops.Remove(shop);
        db.AuditLogs.Add(new AuditLog { Timestamp=DateTimeOffset.UtcNow, UserId=User.Identity?.Name??"Administrator", Action="Shop deleted", EntityType="Shop", EntityId=shop.Id.ToString(), OldValue=JsonSerializer.Serialize(shop), Reason="Deleted from Shop setup", SourceIp=HttpContext.Connection.RemoteIpAddress?.ToString()??"" });
        await db.SaveChangesAsync();
        TempData["Message"] = $"Shop {shop.ShopNumber} deleted.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Shops = await db.Shops.OrderBy(x => x.ShopNumber).ToListAsync();
        await LoadCountsAsync();
    }

    private async Task LoadCountsAsync()
    {
        MeterCounts = await db.Meters.GroupBy(x => x.ShopId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        InvoiceCounts = await db.Invoices.GroupBy(x => x.ShopId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
    }
}
