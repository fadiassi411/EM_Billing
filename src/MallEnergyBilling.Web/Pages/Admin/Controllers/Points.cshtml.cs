using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages.Admin.Controllers;

public sealed class PointsModel(ApplicationDbContext db, IBacnetIpService bacnet, ILogger<PointsModel> log) : PageModel
{
    public Meter Meter { get; private set; } = null!;
    [BindProperty] public BacnetPoint Input { get; set; } = new();
    public IReadOnlyList<BacnetObjectInfo> Objects { get; private set; } = [];
    public BacnetPointResult? TestResult { get; private set; }
    public string Message { get; private set; } = "";
    private async Task<bool> Load(int id)
    {
        Meter = (await db.Meters.Include(x => x.Controller).Include(x => x.BacnetPoints).FirstOrDefaultAsync(x => x.Id == id))!;
        return Meter?.Controller?.CommunicationType == "BacnetIp";
    }
    public async Task<IActionResult> OnGetAsync(int id, int? pointId)
    {
        if (!await Load(id)) return NotFound();
        if (pointId.HasValue)
        {
            var p = Meter.BacnetPoints.FirstOrDefault(x => x.Id == pointId);
            if (p is null) return NotFound();
            Input = new() { Id=p.Id, Name=p.Name, Measurement=p.Measurement, ObjectType=p.ObjectType, ObjectInstance=p.ObjectInstance, PropertyId=p.PropertyId, Unit=p.Unit, Scale=p.Scale, Offset=p.Offset, Enabled=p.Enabled, BillingConfirmed=p.BillingConfirmed };
        }
        return Page();
    }
    private bool ValidateInput()
    {
        try { BacnetIpService.ValidatePoint(Input); }
        catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
        if (Meter.BacnetPoints.Any(p => p.Id != Input.Id && p.ObjectType == Input.ObjectType && p.ObjectInstance == Input.ObjectInstance && p.PropertyId == Input.PropertyId))
            ModelState.AddModelError("", "This object/property is already configured on the meter.");
        if (Input.Enabled && Input.Measurement != MeasurementType.Unmapped && Meter.BacnetPoints.Any(p => p.Id != Input.Id && p.Enabled && p.Measurement == Input.Measurement))
            ModelState.AddModelError("", "Only one enabled point can map to each measurement on a meter.");
        return ModelState.IsValid;
    }
    public async Task<IActionResult> OnPostSaveAsync(int id)
    {
        if (!await Load(id)) return NotFound();
        if (!ValidateInput()) return Page();
        var p = Input.Id == 0 ? new BacnetPoint { MeterId = id } : Meter.BacnetPoints.FirstOrDefault(x => x.Id == Input.Id);
        if (p is null) return NotFound();
        if (Input.Id == 0) db.BacnetPoints.Add(p);
        var old = $"{p.ObjectType}:{p.ObjectInstance}/{p.PropertyId}; {p.Measurement}; {p.Scale}; {p.Offset}";
        p.Name=Input.Name.Trim(); p.Measurement=Input.Measurement; p.ObjectType=Input.ObjectType; p.ObjectInstance=Input.ObjectInstance;
        p.PropertyId=Input.PropertyId; p.Unit=Input.Unit.Trim(); p.Scale=Input.Scale; p.Offset=Input.Offset; p.Enabled=Input.Enabled;
        p.BillingConfirmed=Input.Measurement==MeasurementType.TotalImportEnergy && Input.BillingConfirmed;
        p.Quality="Not read"; p.LastError="Configuration changed; awaiting fresh read.";
        Meter.CommunicationStatus="Awaiting poll";
        db.AuditLogs.Add(new() { Timestamp=DateTimeOffset.UtcNow, UserId=User.Identity?.Name??"Administrator", Action="BACnet point saved", EntityType="Meter", EntityId=id.ToString(), OldValue=old, NewValue=$"{p.Name}: {p.ObjectType}:{p.ObjectInstance}/{p.PropertyId}; {p.Measurement}; scale {p.Scale}; offset {p.Offset}; billing confirmed {p.BillingConfirmed}" });
        await db.SaveChangesAsync(); return RedirectToPage(new { id });
    }
    public async Task<IActionResult> OnPostDeleteAsync(int id, int pointId)
    {
        if (!await Load(id)) return NotFound();
        var point = Meter.BacnetPoints.FirstOrDefault(x => x.Id == pointId); if (point is null) return NotFound();
        db.BacnetPoints.Remove(point); Meter.CommunicationStatus="Awaiting poll";
        db.AuditLogs.Add(new() { Timestamp=DateTimeOffset.UtcNow, UserId=User.Identity?.Name??"Administrator", Action="BACnet point deleted", EntityType="Meter", EntityId=id.ToString(), OldValue=$"{point.Name}: {point.ObjectType}:{point.ObjectInstance}/{point.PropertyId}" });
        await db.SaveChangesAsync(); return RedirectToPage(new { id });
    }
    public async Task<IActionResult> OnPostTestAsync(int id)
    {
        if (!await Load(id)) return NotFound();
        if (!ValidateInput()) return Page();
        try
        {
            await bacnet.TestAsync(Meter.Controller!, HttpContext.RequestAborted);
            // Test disabled points without persisting or enabling them.
            var enabled=Input.Enabled; Input.Enabled=true;
            try { TestResult=(await bacnet.ReadPointsAsync(Meter.Controller!, [Input], HttpContext.RequestAborted)).Single(); } finally { Input.Enabled=enabled; }
        }
        catch (Exception ex) { log.LogWarning(ex, "BACnet commissioning test failed for meter {Meter}", id); Message=BacnetIpService.Error(ex); }
        return Page();
    }
    public async Task<IActionResult> OnPostBrowseAsync(int id)
    {
        if (!await Load(id)) return NotFound();
        ModelState.Clear(); Input=new();
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted); deadline.CancelAfter(TimeSpan.FromMinutes(2));
        try { Objects=await bacnet.BrowseAsync(Meter.Controller!, deadline.Token); Message=$"{Objects.Count} objects returned. Select a point and confirm its measurement mapping."; }
        catch (OperationCanceledException) { Message="Object browse stopped after its time limit. Use manual point entry for large or restricted devices."; }
        catch (Exception ex) { log.LogWarning(ex, "BACnet object browse failed for meter {Meter}", id); Message=BacnetIpService.Error(ex); }
        return Page();
    }
}
