using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages.Water;

public sealed class IndexModel(ApplicationDbContext db, SystemFeatureService features) : PageModel
{
    [BindProperty(SupportsGet=true,Name="q")] public string Search { get; set; } = "";
    public IReadOnlyList<Meter> Meters { get; private set; } = [];
    public async Task<IActionResult> OnGetAsync()
    {
        if (!await features.WaterBillingEnabledAsync()) return NotFound();
        var meters=await db.Meters.AsNoTracking().Include(x=>x.Shop).Include(x=>x.Controller)
            .Where(x=>x.UtilityType==UtilityType.Water).OrderBy(x=>x.Name).ToListAsync();
        Search=Search?.Trim()??"";
        if(Search.Length>0) meters=meters.Where(x=>new[]{x.Id.ToString(),x.Name,x.SerialNumber,x.Model,x.Shop?.ShopNumber,x.Shop?.Name,x.Shop?.TenantName,x.Shop?.Floor,x.Shop?.Zone,x.Controller?.Name,x.Controller?.IpAddress}.Any(v=>!string.IsNullOrWhiteSpace(v)&&v.Contains(Search,StringComparison.OrdinalIgnoreCase))).ToList();
        Meters=meters;return Page();
    }
}
