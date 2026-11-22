using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages.Admin.Controllers;

public sealed class MeterModel(ApplicationDbContext db, LicenseService licenses, IMBusService mbus, ILogger<MeterModel> log) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public List<Shop> Shops { get; private set; } = [];
    public string ControllerName { get; private set; } = "";
    public string ControllerCommunicationType { get; private set; } = "";
    public bool IsMBus => ControllerCommunicationType == "MBusTcp";
    /// <summary>Result of "Read meter" on an M-Bus meter: the meter's answer and the record that would be billed.</summary>
    public MBusTelegram? MBusAnswer { get; private set; }
    public int? MBusBilledRecord { get; private set; }
    public string? MBusReadError { get; private set; }
    /// <summary>The meter's last accepted reading (with the saved offset), used by "Match the meter display".</summary>
    public decimal LastReading { get; private set; }
    public DateTimeOffset? LastReadingAt { get; private set; }
    public decimal SavedOffset { get; private set; }
    public string Unit => Input.UtilityType == UtilityType.Water ? "m³" : "kWh";

    public sealed class InputModel
    {
        public int Id { get; set; }
        [Required] public string Name { get; set; } = "";
        [Required] public string SerialNumber { get; set; } = "";
        [Range(1, int.MaxValue)] public int ShopId { get; set; }
        public UtilityType UtilityType { get; set; } = UtilityType.Electricity;
        public PowerSource PowerSource { get; set; } = PowerSource.Grid;
        public bool Active { get; set; }
        [Range(0, 65535)] public int StartingRegister { get; set; }
        [Required] public string BacnetObjectType { get; set; } = "AnalogInput";
        [Range(0, 4194303)] public int BacnetObjectInstance { get; set; }
        public RegisterDataType DataType { get; set; }
        public WordOrder WordOrder { get; set; }
        [Range(typeof(decimal), "0.00000001", "1000000")] public decimal ScalingFactor { get; set; }
        [Range(1, 1000000)] public int PulseConstant { get; set; }
        [Range(typeof(decimal), "0.0001", "1000000")] public decimal CtPrimary { get; set; }
        [Range(typeof(decimal), "0.0001", "1000000")] public decimal CtSecondary { get; set; }
        public bool CtAppliedByPlc { get; set; }
        [StringLength(20)] public string? MBusSecondaryAddress { get; set; } = "";
        [Range(0, 250)] public int MBusRecord { get; set; }
        [Range(typeof(decimal), "-1000000000", "1000000000")] public decimal Offset { get; set; }
        public string? Notes { get; set; } = "";
        public string? Reason { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await LoadShops();
        var m = await db.Meters.Include(x => x.Controller).FirstOrDefaultAsync(x => x.Id == id);
        if (m is null) return NotFound();
        SetController(m);
        Input = new()
        {
            Id = m.Id, Name = m.Name, SerialNumber = m.SerialNumber, ShopId = m.ShopId, UtilityType = m.UtilityType, PowerSource = m.PowerSource, Active = m.Active,
            StartingRegister = m.StartingRegister, BacnetObjectType = m.BacnetObjectType, BacnetObjectInstance = m.BacnetObjectInstance, DataType = m.DataType, WordOrder = m.WordOrder,
            ScalingFactor = m.ScalingFactor, PulseConstant = m.PulseConstant, CtPrimary = m.CtPrimary, CtSecondary = m.CtSecondary, CtAppliedByPlc = m.CtAppliedByPlc,
            MBusSecondaryAddress = m.MBusSecondaryAddress, MBusRecord = m.MBusRecord, Offset = m.Offset, Notes = m.Notes
        };
        return Page();
    }

    /// <summary>Reads the M-Bus meter with the address typed on the page (saved or not) and lists all of its records.</summary>
    public async Task<IActionResult> OnPostReadMBusAsync()
    {
        await LoadShops();
        var m = await db.Meters.Include(x => x.Controller).FirstOrDefaultAsync(x => x.Id == Input.Id);
        if (m is null) return NotFound();
        SetController(m);
        ModelState.Clear();
        if (!IsMBus || m.Controller is null) return Page();
        var error = ValidateMBusAddress();
        if (error is not null) { MBusReadError = error; return Page(); }
        try
        {
            var result = await mbus.ReadAsync(m.Controller, Input.StartingRegister, Input.MBusSecondaryAddress, HttpContext.RequestAborted);
            MBusAnswer = result.Telegram;
            try { MBusBilledRecord = MBusProtocol.SelectBillingRecord(result.Telegram, Input.UtilityType == UtilityType.Water ? MBusQuantity.Volume : MBusQuantity.Energy, Input.MBusRecord).Index; }
            catch (MBusProtocolException ex) { MBusReadError = ex.Message; }
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { return new EmptyResult(); }
        catch (Exception ex) { log.LogWarning(ex, "M-Bus read of meter {Meter} failed", m.Name); MBusReadError = ex.Message; }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadShops();
        var m = await db.Meters.Include(x => x.Controller).FirstOrDefaultAsync(x => x.Id == Input.Id);
        if (m is null) return NotFound();
        SetController(m);
        var secondary = "";
        if (IsMBus)
        {
            if (ValidateMBusAddress() is { } error) ModelState.AddModelError("Input.StartingRegister", error);
            secondary = MBusProtocol.NormalizeSecondaryAddress(Input.MBusSecondaryAddress) ?? "";
        }
        if (!ModelState.IsValid) return Page();
        if (await db.Meters.AnyAsync(x => x.Id != m.Id && x.SerialNumber == Input.SerialNumber)) { ModelState.AddModelError("Input.SerialNumber", "Meter serial number must be unique."); return Page(); }
        var duplicate = ControllerCommunicationType switch
        {
            "BacnetIp" => false,
            "MBusTcp" when secondary.Length > 0 => await db.Meters.AnyAsync(x => x.Id != m.Id && x.ControllerId == m.ControllerId && x.MBusSecondaryAddress == secondary),
            "MBusTcp" => await db.Meters.AnyAsync(x => x.Id != m.Id && x.ControllerId == m.ControllerId && x.MBusSecondaryAddress == "" && x.StartingRegister == Input.StartingRegister),
            _ => await db.Meters.AnyAsync(x => x.Id != m.Id && x.ControllerId == m.ControllerId && x.StartingRegister == Input.StartingRegister)
        };
        if (duplicate)
        {
            ModelState.AddModelError("", IsMBus ? "Another meter on this gateway already uses this M-Bus address." : "This register is already assigned on the controller.");
            return Page();
        }
        if (Input.Active && !m.Active)
        {
            var activeMeters = await db.Meters.CountAsync(x => x.Active);
            var license = licenses.GetStatus(activeMeters);
            if (activeMeters >= license.MaximumMeters)
            {
                ModelState.AddModelError("Input.Active", $"The {license.Status.ToLowerInvariant()} license allows {license.MaximumMeters} active meters. Open Settings > License to increase capacity.");
                return Page();
            }
        }
        var measurementChanged = m.StartingRegister != Input.StartingRegister || m.BacnetObjectType != Input.BacnetObjectType || m.BacnetObjectInstance != Input.BacnetObjectInstance
            || m.DataType != Input.DataType || m.WordOrder != Input.WordOrder || m.ScalingFactor != Input.ScalingFactor
            || m.MBusSecondaryAddress != secondary || m.MBusRecord != Input.MBusRecord || m.Offset != Input.Offset
            || IsMBus && m.UtilityType != Input.UtilityType; // energy vs volume record
        var old = JsonSerializer.Serialize(m);
        m.Name = Input.Name.Trim(); m.SerialNumber = Input.SerialNumber.Trim(); m.ShopId = Input.ShopId; m.UtilityType = Input.UtilityType; m.PowerSource = Input.PowerSource; m.Active = Input.Active;
        // A new register, address or scale must not be billed as a jump.
        if (measurementChanged && await db.MeterReadings.AnyAsync(x => x.MeterId == m.Id)) m.RebaseOnNextReading = true;
        m.StartingRegister = Input.StartingRegister; m.BacnetObjectType = Input.BacnetObjectType; m.BacnetObjectInstance = Input.BacnetObjectInstance; m.DataType = Input.DataType; m.WordOrder = Input.WordOrder;
        m.ScalingFactor = Input.ScalingFactor; m.PulseConstant = Input.PulseConstant; m.CtPrimary = Input.CtPrimary; m.CtSecondary = Input.CtSecondary; m.CtAppliedByPlc = Input.CtAppliedByPlc;
        m.MBusSecondaryAddress = secondary; m.MBusRecord = Input.MBusRecord; m.Offset = Input.Offset; m.Notes = Input.Notes?.Trim() ?? "";
        if (m.CommunicationStatus != "Counter Review" || !m.Active) m.CommunicationStatus = m.Active ? "Awaiting poll" : "Not commissioned";
        db.AuditLogs.Add(new() { Timestamp = DateTimeOffset.UtcNow, UserId = User.Identity?.Name ?? "Administrator", Action = "Meter channel configuration changed", EntityType = "Meter", EntityId = m.Id.ToString(), OldValue = old, NewValue = JsonSerializer.Serialize(Input), Reason = string.IsNullOrWhiteSpace(Input.Reason) ? "Not provided" : Input.Reason.Trim(), SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "" });
        await db.SaveChangesAsync();
        return RedirectToPage("Channels", new { id = m.ControllerId });
    }

    string? ValidateMBusAddress()
    {
        if (!string.IsNullOrWhiteSpace(Input.MBusSecondaryAddress) && MBusProtocol.NormalizeSecondaryAddress(Input.MBusSecondaryAddress) is null)
            return "The M-Bus secondary address (ID) must be the 8 digits printed on the meter, or empty.";
        if (string.IsNullOrWhiteSpace(Input.MBusSecondaryAddress) && Input.StartingRegister is < 0 or > 250)
            return "The M-Bus primary address must be between 0 and 250.";
        return null;
    }

    void SetController(Meter m) { ControllerName = m.Controller?.Name ?? ""; ControllerCommunicationType = m.Controller?.CommunicationType ?? ""; LastReading = m.LastReading; LastReadingAt = m.LastReadingAt; SavedOffset = m.Offset; }
    async Task LoadShops() => Shops = await db.Shops.OrderBy(x => x.ShopNumber).ToListAsync();
}
