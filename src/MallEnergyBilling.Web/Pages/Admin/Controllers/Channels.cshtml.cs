using System.ComponentModel.DataAnnotations;
using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages.Admin.Controllers;

public sealed class ChannelsModel(ApplicationDbContext db) : PageModel
{
    public Models.Controller Controller { get; private set; } = null!;
    public List<Meter> Meters { get; private set; } = [];
    public List<Shop> Shops { get; private set; } = [];
    [BindProperty, Range(0, 65535)] public int FirstRegister { get; set; } = 4196;
    [BindProperty, Range(1, 100)] public int RegisterStride { get; set; } = 2;
    [BindProperty] public string BacnetObjectType { get; set; } = "AnalogInput";
    [BindProperty, Range(0, 4194303)] public int FirstBacnetObjectInstance { get; set; }
    [BindProperty, Range(1, 1000)] public int BacnetObjectStride { get; set; } = 1;
    [BindProperty, Range(1, 45)] public int ChannelCount { get; set; } = 45;
    [BindProperty] public RegisterDataType DataType { get; set; } = RegisterDataType.UInt32;
    [BindProperty] public WordOrder WordOrder { get; set; } = WordOrder.LowHigh;
    [BindProperty] public PowerSource PowerSource { get; set; } = PowerSource.Grid;
    [BindProperty] public UtilityType UtilityType { get; set; } = UtilityType.Electricity;
    [BindProperty, Range(typeof(decimal), "0.00000001", "1000000")] public decimal ScalingFactor { get; set; } = .01m;
    [BindProperty, Range(1, int.MaxValue)] public int ShopId { get; set; }
    [BindProperty, StringLength(40)] public string MeterNamePrefix { get; set; } = "Meter";
    [BindProperty, StringLength(100)] public string FirstMeterSerial { get; set; } = "";
    [BindProperty, StringLength(300)] public string Reason { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(int id) { if (!await Load(id)) return NotFound(); ShopId = Shops.FirstOrDefault()?.Id ?? 0; ChannelCount = Math.Max(1, 45 - Meters.Count); return Page(); }
    public async Task<IActionResult> OnPostGenerateAsync(int id)
    {
        if (!await Load(id)) return NotFound();
        if (!await db.Shops.AnyAsync(x => x.Id == ShopId)) ModelState.AddModelError(nameof(ShopId), "Select a temporary or final shop assignment.");
        if (Meters.Count + ChannelCount > 45) ModelState.AddModelError(nameof(ChannelCount), $"This controller already has {Meters.Count} channels; maximum is 45.");
        var isBacnet = Controller.CommunicationType == "BacnetIp";
        var lastRegister = FirstRegister + (ChannelCount - 1) * RegisterStride;
        if (!isBacnet && lastRegister > 65535) ModelState.AddModelError(nameof(FirstRegister), "The generated register range exceeds 65535.");
        var lastObject = FirstBacnetObjectInstance + (ChannelCount - 1) * BacnetObjectStride;
        if (isBacnet && lastObject > 4194303) ModelState.AddModelError(nameof(FirstBacnetObjectInstance), "The generated BACnet object range exceeds 4194303.");
        if (!ModelState.IsValid) return Page();
        var existingRegisters = Meters.Select(x => x.StartingRegister).ToHashSet();
        var existingObjects = Meters.Select(x => $"{x.BacnetObjectType}:{x.BacnetObjectInstance}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ChannelCount; i++)
        {
            var register = FirstRegister + i * RegisterStride;
            var objectInstance = FirstBacnetObjectInstance + i * BacnetObjectStride;
            if (!isBacnet && existingRegisters.Contains(register)) { ModelState.AddModelError("", $"Register {register} is already assigned on this controller."); return Page(); }
            if (isBacnet && existingObjects.Contains($"{BacnetObjectType}:{objectInstance}")) { ModelState.AddModelError("", $"BACnet object {BacnetObjectType}:{objectInstance} is already assigned on this controller."); return Page(); }
        }
        var startChannel = Meters.Count + 1;
        for (var i = 0; i < ChannelCount; i++)
        {
            var channel = startChannel + i;
            var serial = i == 0 && !string.IsNullOrWhiteSpace(FirstMeterSerial) ? FirstMeterSerial.Trim() : $"PENDING-{Controller.Id}-{channel:00}-{Guid.NewGuid():N}";
            db.Meters.Add(new Meter { ControllerId=id, ShopId=ShopId, Name=$"{(string.IsNullOrWhiteSpace(MeterNamePrefix)?"Meter":MeterNamePrefix.Trim())}-{channel:00}", SerialNumber=serial, UtilityType=UtilityType, PowerSource=PowerSource, StartingRegister=FirstRegister+i*RegisterStride, BacnetObjectType=BacnetObjectType, BacnetObjectInstance=FirstBacnetObjectInstance+i*BacnetObjectStride, DataType=DataType, WordOrder=WordOrder, ScalingFactor=ScalingFactor, PulseConstant=1600, Active=false, CommunicationStatus="Not commissioned", Notes="Generated channel; replace pending serial number before activation." });
        }
        db.AuditLogs.Add(new(){Timestamp=DateTimeOffset.UtcNow,UserId=User.Identity?.Name??"Administrator",Action="Controller channels generated",EntityType="Controller",EntityId=id.ToString(),NewValue=isBacnet?$"{ChannelCount} {UtilityType} channels; source {PowerSource}; BACnet {BacnetObjectType} from {FirstBacnetObjectInstance}; stride {BacnetObjectStride}; scale {ScalingFactor}":$"{ChannelCount} {UtilityType} channels; source {PowerSource}; first register {FirstRegister}; stride {RegisterStride}; {DataType}; scale {ScalingFactor}",Reason=string.IsNullOrWhiteSpace(Reason)?"Not provided":Reason.Trim(),SourceIp=HttpContext.Connection.RemoteIpAddress?.ToString()??""});
        await db.SaveChangesAsync(); return RedirectToPage(new { id });
    }
    public async Task<IActionResult> OnPostDeleteAsync(int id, int meterId)
    {
        var meter = await db.Meters.FirstOrDefaultAsync(x => x.Id == meterId && x.ControllerId == id);
        if (meter is null) return NotFound();
        if (await db.MeterReadings.AnyAsync(x => x.MeterId == meterId) || await db.Invoices.AnyAsync(x => x.MeterId == meterId) || await db.Tariffs.AnyAsync(x => x.MeterId == meterId))
        {
            TempData["Error"] = $"Channel {meter.Name} has reading, tariff, or invoice history and cannot be deleted. Deactivate it instead.";
            return RedirectToPage(new { id });
        }
        db.AuditLogs.Add(new(){Timestamp=DateTimeOffset.UtcNow,UserId=User.Identity?.Name??"Administrator",Action="Meter channel deleted",EntityType="Meter",EntityId=meterId.ToString(),OldValue=$"{meter.Name}; {meter.SerialNumber}; register {meter.StartingRegister}",Reason="Administrator confirmed channel deletion",SourceIp=HttpContext.Connection.RemoteIpAddress?.ToString()??""});
        db.Meters.Remove(meter); await db.SaveChangesAsync(); TempData["Success"]=$"Channel {meter.Name} was deleted."; return RedirectToPage(new { id });
    }
    public async Task<IActionResult> OnPostArchiveAsync(int id, int meterId)
    {
        if (!User.IsInRole("Administrator")) return Forbid();
        var meter = await db.Meters.FirstOrDefaultAsync(x => x.Id == meterId && x.ControllerId == id);
        if (meter is null) return NotFound();
        meter.Active = false;
        meter.CommunicationStatus = "Archived";
        db.AuditLogs.Add(new(){Timestamp=DateTimeOffset.UtcNow,UserId=User.Identity?.Name??"Administrator",Action="Meter archived",EntityType="Meter",EntityId=meter.Id.ToString(),OldValue=$"{meter.Name}; {meter.SerialNumber}",NewValue="Archived and inactive",Reason="Administrator confirmed archive",SourceIp=HttpContext.Connection.RemoteIpAddress?.ToString()??""});
        await db.SaveChangesAsync();
        TempData["Success"]=$"{meter.Name} was archived. Its readings, tariffs, and invoices were preserved.";
        return RedirectToPage(new { id });
    }
    public async Task<IActionResult> OnPostRestoreMeterAsync(int id, int meterId)
    {
        if (!User.IsInRole("Administrator")) return Forbid();
        var meter = await db.Meters.FirstOrDefaultAsync(x => x.Id == meterId && x.ControllerId == id && x.CommunicationStatus == "Archived");
        if (meter is null) return NotFound();
        meter.CommunicationStatus = "Not commissioned";
        db.AuditLogs.Add(new(){Timestamp=DateTimeOffset.UtcNow,UserId=User.Identity?.Name??"Administrator",Action="Meter restored from archive",EntityType="Meter",EntityId=meter.Id.ToString(),OldValue="Archived",NewValue="Not commissioned",Reason="Administrator restored meter",SourceIp=HttpContext.Connection.RemoteIpAddress?.ToString()??""});
        await db.SaveChangesAsync();
        TempData["Success"]=$"{meter.Name} was restored. Commission it again when ready.";
        return RedirectToPage(new { id });
    }
    public async Task<IActionResult> OnPostPermanentDeleteAsync(int id, int meterId, string confirmation)
    {
        if (!User.IsInRole("Administrator")) return Forbid();
        if (!string.Equals(confirmation, "DELETE", StringComparison.Ordinal))
        {
            TempData["Error"]="Permanent deletion was cancelled. Type DELETE exactly to confirm.";
            return RedirectToPage(new { id });
        }
        var meter = await db.Meters.FirstOrDefaultAsync(x => x.Id == meterId && x.ControllerId == id);
        if (meter is null) return NotFound();
        var snapshot=$"{meter.Name}; serial {meter.SerialNumber}; controller {meter.ControllerId}; shop {meter.ShopId}; register {meter.StartingRegister}";
        await using var transaction = await db.Database.BeginTransactionAsync();
        var invoiceIds = await db.Invoices.Where(x => x.MeterId == meterId).Select(x => x.Id).ToListAsync();
        if (invoiceIds.Count > 0) db.Payments.RemoveRange(await db.Payments.Where(x => invoiceIds.Contains(x.InvoiceId)).ToListAsync());
        db.Invoices.RemoveRange(await db.Invoices.Where(x => x.MeterId == meterId).ToListAsync());
        db.MeterReadings.RemoveRange(await db.MeterReadings.Where(x => x.MeterId == meterId).ToListAsync());
        db.Tariffs.RemoveRange(await db.Tariffs.Where(x => x.MeterId == meterId).ToListAsync());
        db.AuditLogs.RemoveRange(await db.AuditLogs.Where(x => x.EntityType == "Meter" && x.EntityId == meterId.ToString()).ToListAsync());
        db.Meters.Remove(meter);
        db.AuditLogs.Add(new(){Timestamp=DateTimeOffset.UtcNow,UserId=User.Identity?.Name??"Administrator",Action="Meter permanently deleted",EntityType="DeletedMeter",EntityId=meterId.ToString(),OldValue=snapshot,NewValue="Meter and all dependent billing records deleted",Reason="Administrator typed DELETE",SourceIp=HttpContext.Connection.RemoteIpAddress?.ToString()??""});
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["Success"]=$"{meter.Name} and all of its readings, tariffs, invoices, and payments were permanently deleted.";
        return RedirectToPage(new { id });
    }
    private async Task<bool> Load(int id) { Controller=await db.Controllers.FindAsync(id) ?? null!; if(Controller is null)return false;Meters=await db.Meters.Where(x=>x.ControllerId==id).OrderBy(x=>x.StartingRegister).ToListAsync();Shops=await db.Shops.OrderBy(x=>x.ShopNumber).ToListAsync();return true; }
}
