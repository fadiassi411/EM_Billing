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
        Input.ShopNumber = Input.ShopNumber.Trim().ToUpperInvariant();
        if (await db.Shops.AnyAsync(x => x.ShopNumber == Input.ShopNumber))
            ModelState.AddModelError("Input.ShopNumber", "This shop number already exists.");
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }

        var shop = new Shop
        {
            ShopNumber=Input.ShopNumber, Name=Input.Name.Trim(), TenantName=Input.TenantName.Trim(),
            ContactPerson=Input.ContactPerson.Trim(), Telephone=Input.Telephone.Trim(), Email=Input.Email.Trim(),
            Floor=Input.Floor.Trim(), Zone=Input.Zone.Trim(), MdbPanel=Input.MdbPanel.Trim(), Status=Input.Status,
            BillingAddress=Input.BillingAddress.Trim(), TaxNumber=Input.TaxNumber.Trim(), Notes=Input.Notes.Trim()
        };
        db.Shops.Add(shop);
        await db.SaveChangesAsync();
        db.AuditLogs.Add(new AuditLog { Timestamp=DateTimeOffset.UtcNow, UserId=User.Identity?.Name??"Administrator", Action="Shop created", EntityType="Shop", EntityId=shop.Id.ToString(), NewValue=JsonSerializer.Serialize(shop), Reason=string.IsNullOrWhiteSpace(Input.Reason)?"Not provided":Input.Reason.Trim(), SourceIp=HttpContext.Connection.RemoteIpAddress?.ToString()??"" });
        await db.SaveChangesAsync();
        TempData["Message"] = $"Shop {shop.ShopNumber} added.";
        return RedirectToPage();
    }

    private async Task LoadAsync() => Shops = await db.Shops.OrderBy(x => x.ShopNumber).ToListAsync();
}
