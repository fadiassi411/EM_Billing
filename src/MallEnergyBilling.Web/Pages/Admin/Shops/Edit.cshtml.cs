using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages.Admin.Shops;

public sealed class EditModel(ApplicationDbContext db) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Range(1, int.MaxValue)] public int Id { get; set; }
        [Required, StringLength(40)] public string ShopNumber { get; set; } = "";
        [Required, StringLength(100)] public string Name { get; set; } = "";
        [StringLength(100)] public string TenantName { get; set; } = "";
        [StringLength(40)] public string ContactPerson { get; set; } = "";
        [StringLength(30)] public string Telephone { get; set; } = "";
        [EmailAddress, StringLength(100)] public string Email { get; set; } = "";
        [StringLength(30)] public string Floor { get; set; } = "";
        [StringLength(30)] public string Zone { get; set; } = "";
        [StringLength(40)] public string MdbPanel { get; set; } = "";
        public ShopStatus Status { get; set; }
        [StringLength(200)] public string BillingAddress { get; set; } = "";
        [StringLength(40)] public string TaxNumber { get; set; } = "";
        [StringLength(500)] public string Notes { get; set; } = "";
        [StringLength(300)] public string Reason { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var shop = await db.Shops.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (shop is null) return NotFound();
        Input = new()
        {
            Id=shop.Id, ShopNumber=shop.ShopNumber, Name=shop.Name, TenantName=shop.TenantName,
            ContactPerson=shop.ContactPerson, Telephone=shop.Telephone, Email=shop.Email,
            Floor=shop.Floor, Zone=shop.Zone, MdbPanel=shop.MdbPanel, Status=shop.Status,
            BillingAddress=shop.BillingAddress, TaxNumber=shop.TaxNumber, Notes=shop.Notes
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var shop = await db.Shops.FirstOrDefaultAsync(x => x.Id == Input.Id);
        if (shop is null) return NotFound();

        Input.ShopNumber = (Input.ShopNumber ?? "").Trim().ToUpperInvariant();
        if (await db.Shops.AnyAsync(x => x.Id != Input.Id && x.ShopNumber == Input.ShopNumber))
            ModelState.AddModelError("Input.ShopNumber", "This shop number already exists.");
        if (!ModelState.IsValid) return Page();

        var oldValue = JsonSerializer.Serialize(shop);
        shop.ShopNumber=Input.ShopNumber;
        shop.Name=Input.Name.Trim();
        shop.TenantName=Input.TenantName?.Trim()??"";
        shop.ContactPerson=Input.ContactPerson?.Trim()??"";
        shop.Telephone=Input.Telephone?.Trim()??"";
        shop.Email=Input.Email?.Trim()??"";
        shop.Floor=Input.Floor?.Trim()??"";
        shop.Zone=Input.Zone?.Trim()??"";
        shop.MdbPanel=Input.MdbPanel?.Trim()??"";
        shop.Status=Input.Status;
        shop.BillingAddress=Input.BillingAddress?.Trim()??"";
        shop.TaxNumber=Input.TaxNumber?.Trim()??"";
        shop.Notes=Input.Notes?.Trim()??"";

        db.AuditLogs.Add(new AuditLog
        {
            Timestamp=DateTimeOffset.UtcNow,
            UserId=User.Identity?.Name??"Administrator",
            Action="Shop details changed",
            EntityType="Shop",
            EntityId=shop.Id.ToString(),
            OldValue=oldValue,
            NewValue=JsonSerializer.Serialize(shop),
            Reason=string.IsNullOrWhiteSpace(Input.Reason)?"Not provided":Input.Reason.Trim(),
            SourceIp=HttpContext.Connection.RemoteIpAddress?.ToString()??""
        });
        await db.SaveChangesAsync();
        TempData["Message"] = $"Shop {shop.ShopNumber} updated.";
        return RedirectToPage("Index");
    }
}
