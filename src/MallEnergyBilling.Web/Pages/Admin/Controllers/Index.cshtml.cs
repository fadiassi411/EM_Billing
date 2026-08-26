using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages.Admin.Controllers;
public sealed class IndexModel(ApplicationDbContext db) : PageModel
{
    public List<Models.Controller> Controllers { get; private set; } = [];
    public async Task OnGetAsync() => Controllers = await db.Controllers.OrderBy(x => x.Name).ToListAsync();
    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var controller = await db.Controllers.FindAsync(id);
        if (controller is null) return NotFound();
        if (await db.Meters.AnyAsync(x => x.ControllerId == id))
        {
            TempData["Error"] = "Controller cannot be deleted while it has meter channels. Delete or reassign its channels first.";
            return RedirectToPage();
        }
        db.AuditLogs.Add(new AuditLog { Timestamp=DateTimeOffset.UtcNow, UserId=User.Identity?.Name??"Administrator", Action="Controller deleted", EntityType="Controller", EntityId=id.ToString(), OldValue=$"{controller.Name}; {controller.MdbPanel}; {controller.CommunicationType}; {controller.ComPort}; slave {controller.SlaveAddress}", Reason="Administrator confirmed controller deletion", SourceIp=HttpContext.Connection.RemoteIpAddress?.ToString()??"" });
        db.Controllers.Remove(controller); await db.SaveChangesAsync(); TempData["Success"] = $"Controller {controller.Name} was deleted."; return RedirectToPage();
    }
}
