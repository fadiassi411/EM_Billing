using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages.Admin.Features;

public sealed class IndexModel(ApplicationDbContext db) : PageModel
{
    [BindProperty] public bool WaterBillingEnabled { get; set; }

    public async Task OnGetAsync() => WaterBillingEnabled = await db.SystemFeatureConfigurations
        .AsNoTracking().Where(x => x.Id == 1).Select(x => x.WaterBillingEnabled).FirstOrDefaultAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        var settings = await db.SystemFeatureConfigurations.FirstOrDefaultAsync(x => x.Id == 1);
        if (settings is null) { settings = new SystemFeatureConfiguration { Id = 1 }; db.Add(settings); }
        var previous = settings.WaterBillingEnabled;
        settings.WaterBillingEnabled = WaterBillingEnabled;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        settings.UpdatedBy = User.Identity?.Name ?? "Administrator";
        db.AuditLogs.Add(new(){Timestamp=DateTimeOffset.UtcNow,UserId=settings.UpdatedBy,Action="System feature changed",EntityType="SystemFeatureConfiguration",EntityId="1",OldValue=$"Water billing={previous}",NewValue=$"Water billing={WaterBillingEnabled}",Reason="Administrator feature configuration",SourceIp=HttpContext.Connection.RemoteIpAddress?.ToString()??""});
        await db.SaveChangesAsync();
        TempData["Success"] = WaterBillingEnabled ? "Water metering and billing is enabled." : "Water metering and billing is disabled and hidden.";
        return RedirectToPage();
    }
}
