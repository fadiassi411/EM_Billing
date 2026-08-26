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
    [BindProperty, Range(1, 36)] public int ChannelCount { get; set; } = 36;
    [BindProperty] public RegisterDataType DataType { get; set; } = RegisterDataType.UInt32;
    [BindProperty] public WordOrder WordOrder { get; set; } = WordOrder.LowHigh;
    [BindProperty, Range(typeof(decimal), "0.00000001", "1000000")] public decimal ScalingFactor { get; set; } = .01m;
    [BindProperty, Range(1, int.MaxValue)] public int ShopId { get; set; }
    [BindProperty, StringLength(40)] public string MeterNamePrefix { get; set; } = "Meter";
    [BindProperty, StringLength(100)] public string FirstMeterSerial { get; set; } = "";
    [BindProperty, StringLength(300)] public string Reason { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(int id) { if (!await Load(id)) return NotFound(); ShopId = Shops.FirstOrDefault()?.Id ?? 0; ChannelCount = Math.Max(1, 36 - Meters.Count); return Page(); }
    public async Task<IActionResult> OnPostGenerateAsync(int id)
    {
        if (!await Load(id)) return NotFound();
        if (!await db.Shops.AnyAsync(x => x.Id == ShopId)) ModelState.AddModelError(nameof(ShopId), "Select a temporary or final shop assignment.");
        if (Meters.Count + ChannelCount > 36) ModelState.AddModelError(nameof(ChannelCount), $"This controller already has {Meters.Count} channels; maximum is 36.");
        var lastRegister = FirstRegister + (ChannelCount - 1) * RegisterStride;
        if (lastRegister > 65535) ModelState.AddModelError(nameof(FirstRegister), "The generated register range exceeds 65535.");
        if (!ModelState.IsValid) return Page();
        var existingRegisters = Meters.Select(x => x.StartingRegister).ToHashSet();
        for (var i = 0; i < ChannelCount; i++)
        {
            var register = FirstRegister + i * RegisterStride;
            if (existingRegisters.Contains(register)) { ModelState.AddModelError("", $"Register {register} is already assigned on this controller."); return Page(); }
        }
        var startChannel = Meters.Count + 1;
        for (var i = 0; i < ChannelCount; i++)
        {
            var channel = startChannel + i;
            var serial = i == 0 && !string.IsNullOrWhiteSpace(FirstMeterSerial) ? FirstMeterSerial.Trim() : $"PENDING-{Controller.Id}-{channel:00}-{Guid.NewGuid():N}";
            db.Meters.Add(new Meter { ControllerId=id, ShopId=ShopId, Name=$"{(string.IsNullOrWhiteSpace(MeterNamePrefix)?"Meter":MeterNamePrefix.Trim())}-{channel:00}", SerialNumber=serial, StartingRegister=FirstRegister+i*RegisterStride, DataType=DataType, WordOrder=WordOrder, ScalingFactor=ScalingFactor, PulseConstant=1600, Active=false, CommunicationStatus="Not commissioned", Notes="Generated channel; replace pending serial number before activation." });
        }
        db.AuditLogs.Add(new(){Timestamp=DateTimeOffset.UtcNow,UserId=User.Identity?.Name??"Administrator",Action="Controller channels generated",EntityType="Controller",EntityId=id.ToString(),NewValue=$"{ChannelCount} channels; first register {FirstRegister}; stride {RegisterStride}; {DataType}; scale {ScalingFactor}",Reason=string.IsNullOrWhiteSpace(Reason)?"Not provided":Reason.Trim(),SourceIp=HttpContext.Connection.RemoteIpAddress?.ToString()??""});
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
    private async Task<bool> Load(int id) { Controller=await db.Controllers.FindAsync(id) ?? null!; if(Controller is null)return false;Meters=await db.Meters.Where(x=>x.ControllerId==id).OrderBy(x=>x.StartingRegister).ToListAsync();Shops=await db.Shops.OrderBy(x=>x.ShopNumber).ToListAsync();return true; }
}
