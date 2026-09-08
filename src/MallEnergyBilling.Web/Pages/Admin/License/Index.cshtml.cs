using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages.Admin.License;

public sealed class IndexModel(ApplicationDbContext db, LicenseService licenses) : PageModel
{
    [BindProperty] public IFormFile? LicenseFile { get; set; }
    public LicenseStatus LicenseStatus { get; private set; } = null!;

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostImportAsync()
    {
        var activeMeters = await db.Meters.CountAsync(x => x.Active);
        if (LicenseFile is null || LicenseFile.Length == 0)
        {
            ModelState.AddModelError(nameof(LicenseFile), "Select the .wdlicense file supplied by MicroBrain.");
            LicenseStatus = licenses.GetStatus(activeMeters);
            return Page();
        }
        if (LicenseFile.Length > 64 * 1024)
        {
            ModelState.AddModelError(nameof(LicenseFile), "The license file must be smaller than 64 KB.");
            LicenseStatus = licenses.GetStatus(activeMeters);
            return Page();
        }

        try
        {
            await using var stream = LicenseFile.OpenReadStream();
            var status = await licenses.ImportAsync(stream, activeMeters, HttpContext.RequestAborted);
            db.AuditLogs.Add(new AuditLog
            {
                Timestamp = DateTimeOffset.UtcNow,
                UserId = User.Identity?.Name ?? "Administrator",
                Action = "License imported",
                EntityType = "License",
                EntityId = status.License?.LicenseId.ToString() ?? "",
                NewValue = $"Customer {status.License?.CustomerName}; capacity {status.MaximumMeters}; edition {status.License?.Edition}",
                SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
            });
            await db.SaveChangesAsync();
            TempData["LicenseSuccess"] = $"License activated for {status.License?.CustomerName}.";
            return RedirectToPage();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            ModelState.AddModelError(nameof(LicenseFile), ex.Message);
            LicenseStatus = licenses.GetStatus(activeMeters);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRemoveAsync()
    {
        var oldStatus = licenses.GetStatus(await db.Meters.CountAsync(x => x.Active));
        licenses.Remove();
        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = DateTimeOffset.UtcNow,
            UserId = User.Identity?.Name ?? "Administrator",
            Action = "License removed",
            EntityType = "License",
            EntityId = oldStatus.License?.LicenseId.ToString() ?? "",
            OldValue = $"Customer {oldStatus.License?.CustomerName}; capacity {oldStatus.MaximumMeters}",
            Reason = "Administrator confirmed license removal",
            SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });
        await db.SaveChangesAsync();
        TempData["LicenseSuccess"] = "The paid license was removed. Free mode allows 5 active meters.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var activeMeters = await db.Meters.CountAsync(x => x.Active);
        LicenseStatus = licenses.GetStatus(activeMeters);
    }
}
