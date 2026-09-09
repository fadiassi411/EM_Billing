using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ControllerModel = MallEnergyBilling.Web.Models.Controller;

namespace MallEnergyBilling.Web.Pages.Admin.Controllers;
public sealed class EditModel(ApplicationDbContext db, IModbusService modbus, IBacnetIpService bacnet, ILogger<EditModel> log) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public List<Meter> Meters { get; private set; } = [];
    public string? TestResult { get; private set; }
    public IReadOnlyList<BacnetInterface> Interfaces => BacnetNetwork.Interfaces();
    private ControllerModel BacnetConfig() => new() { IpAddress=Input.IpAddress?.Trim()??"", BacnetDeviceInstance=Input.BacnetDeviceInstance, BacnetLocalIp=Input.BacnetLocalIp?.Trim()??"", BacnetUdpPort=Input.BacnetUdpPort, TimeoutMilliseconds=Input.TimeoutMilliseconds, RetryCount=Input.RetryCount, PollingIntervalSeconds=Input.PollingIntervalSeconds };
    public async Task<IActionResult> OnPostBacnetAsync(string operation)
    {
        try
        {
            var c = BacnetConfig();
            foreach (var key in new[] { "Input.BacnetDeviceInstance", "Input.BacnetUdpPort", "Input.TimeoutMilliseconds", "Input.RetryCount", "Input.PollingIntervalSeconds" })
                if (ModelState.TryGetValue(key, out var state) && state.Errors.Count > 0) throw new InvalidOperationException("Correct the BACnet numeric settings before testing.");
            BacnetNetwork.Validate(c, operation != "discover");
            if (operation == "discover") return new JsonResult(new { devices = await bacnet.DiscoverAsync(c, HttpContext.RequestAborted) });
            if (operation == "test") return new JsonResult(new { device = await bacnet.TestAsync(c, HttpContext.RequestAborted) });
            return BadRequest();
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { return new EmptyResult(); }
        catch (Exception ex) { log.LogWarning(ex, "BACnet commissioning {Operation} failed", operation); return new JsonResult(new { error = BacnetIpService.Error(ex) }); }
    }
    public sealed class InputModel
    {
        public int Id { get; set; }
        [Required, StringLength(80)] public string Name { get; set; } = "";
        [Required, StringLength(40)] public string MdbPanel { get; set; } = "";
        [Required] public string CommunicationType { get; set; } = "ModbusRtu";
        public string ComPort { get; set; } = "COM1";
        [Range(300, 921600)] public int BaudRate { get; set; } = 9600;
        [Required] public string Parity { get; set; } = "Even";
        [Range(7,8)] public int DataBits { get; set; } = 8;
        [Range(1,2)] public int StopBits { get; set; } = 1;
        [StringLength(253)] public string? IpAddress { get; set; } = "";
        [Range(1,65535)] public int TcpPort { get; set; } = 502;
        [StringLength(45)] public string? BacnetLocalIp { get; set; } = "";
        [Range(1,65535)] public int BacnetUdpPort { get; set; } = 47808;
        [Range(0,4194302)] public int? BacnetDeviceInstance { get; set; }
        [Range(1,247)] public byte SlaveAddress { get; set; } = 1;
        [Range(1,3600)] public int PollingIntervalSeconds { get; set; } = 5;
        [Range(100,30000)] public int TimeoutMilliseconds { get; set; } = 1000;
        [Range(0,10)] public int RetryCount { get; set; } = 2;
        public bool Enabled { get; set; } = true;
        public string? Notes { get; set; } = "";
        public bool ConfirmRealMode { get; set; }
        public string? ChangeReason { get; set; } = "";
    }
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null) return Page();
        var x = await db.Controllers.FindAsync(id.Value); if (x is null) return NotFound();
        Input = new(){Id=x.Id,Name=x.Name,MdbPanel=x.MdbPanel,CommunicationType=x.CommunicationType,ComPort=x.ComPort,BaudRate=x.BaudRate,Parity=x.Parity,DataBits=x.DataBits,StopBits=x.StopBits,IpAddress=x.IpAddress,TcpPort=x.TcpPort,BacnetLocalIp=x.BacnetLocalIp,BacnetUdpPort=x.BacnetUdpPort,BacnetDeviceInstance=x.BacnetDeviceInstance,SlaveAddress=x.SlaveAddress,PollingIntervalSeconds=x.PollingIntervalSeconds,TimeoutMilliseconds=x.TimeoutMilliseconds,RetryCount=x.RetryCount,Enabled=x.Enabled,Notes=x.Notes};
        Meters = await db.Meters.Where(m=>m.ControllerId==x.Id).ToListAsync(); return Page();
    }
    public async Task<IActionResult> OnPostSaveAsync()
    {
        ValidateTransport();
        if (Input.CommunicationType == "BacnetIp" && await db.Controllers.AnyAsync(c => c.Id != Input.Id && c.CommunicationType == "BacnetIp" && c.IpAddress == (Input.IpAddress??"").Trim() && c.BacnetUdpPort == Input.BacnetUdpPort))
            ModelState.AddModelError("Input.IpAddress", "This BACnet endpoint is already configured. Add meter points to the existing device.");
        if (!Input.ConfirmRealMode) ModelState.AddModelError("Input.ConfirmRealMode","Confirm that the controller settings are ready for real meter communication.");
        if(!ModelState.IsValid){if(Input.Id>0)Meters=await db.Meters.Where(m=>m.ControllerId==Input.Id).ToListAsync();return Page();}
        ControllerModel x; string old;
        if(Input.Id==0){x=new();db.Controllers.Add(x);old="New controller";}else{x=await db.Controllers.FindAsync(Input.Id)??throw new InvalidOperationException("Controller not found.");old=JsonSerializer.Serialize(x);}
        x.Name=Input.Name.Trim();x.MdbPanel=Input.MdbPanel.Trim();x.CommunicationType=Input.CommunicationType;x.ComPort=Input.ComPort.ToUpperInvariant();x.BaudRate=Input.BaudRate;x.Parity=Input.Parity;x.DataBits=Input.DataBits;x.StopBits=Input.StopBits;x.IpAddress=(Input.IpAddress??"").Trim();x.TcpPort=Input.TcpPort;x.BacnetLocalIp=(Input.BacnetLocalIp??"").Trim();x.BacnetUdpPort=Input.BacnetUdpPort;x.BacnetDeviceInstance=Input.BacnetDeviceInstance;x.SlaveAddress=Input.SlaveAddress;x.PollingIntervalSeconds=Input.PollingIntervalSeconds;x.TimeoutMilliseconds=Input.TimeoutMilliseconds;x.RetryCount=Input.RetryCount;x.Enabled=Input.Enabled;x.Notes=Input.Notes??"";x.Condition="Ready to test";
        db.AuditLogs.Add(new(){Timestamp=DateTimeOffset.UtcNow,UserId=User.Identity?.Name??"Administrator",Action=Input.Id==0?"Controller created":"Controller configuration changed",EntityType="Controller",EntityId=Input.Id.ToString(),OldValue=old,NewValue=JsonSerializer.Serialize(Input),Reason=string.IsNullOrWhiteSpace(Input.ChangeReason)?"Not provided":Input.ChangeReason.Trim(),SourceIp=HttpContext.Connection.RemoteIpAddress?.ToString()??""});await db.SaveChangesAsync();return RedirectToPage("Index");
    }
    public async Task<IActionResult> OnPostTestAsync()
    {
        if (Input.CommunicationType == "BacnetIp")
        {
            try { BacnetNetwork.Validate(BacnetConfig()); var d=await bacnet.TestAsync(BacnetConfig(),HttpContext.RequestAborted); TestResult=$"BACnet connection successful: {d.Name}, Device Instance {d.Instance}, {d.Ip}:{d.Port}"; }
            catch(Exception ex) { log.LogWarning(ex,"BACnet connection test failed"); TestResult=BacnetIpService.Error(ex); }
            return Page();
        }
        if(Input.Id>0)Meters=await db.Meters.Where(m=>m.ControllerId==Input.Id).OrderBy(m=>m.StartingRegister).ToListAsync();
        ValidateTransport();
        if(!ModelState.IsValid){TestResult="FAILED - correct the communication settings below.";return Page();}
        if(Meters.Count==0)TestResult="FAILED - add at least one meter register channel before testing.";
        else
        {
            var meter=Meters[0];var config=new ControllerModel{CommunicationType=Input.CommunicationType,ComPort=Input.ComPort.ToUpperInvariant(),BaudRate=Input.BaudRate,Parity=Input.Parity,DataBits=Input.DataBits,StopBits=Input.StopBits,IpAddress=(Input.IpAddress??"").Trim(),TcpPort=Input.TcpPort,BacnetLocalIp=(Input.BacnetLocalIp??"").Trim(),BacnetUdpPort=Input.BacnetUdpPort,BacnetDeviceInstance=Input.BacnetDeviceInstance,SlaveAddress=Input.SlaveAddress,TimeoutMilliseconds=Input.TimeoutMilliseconds,RetryCount=Input.RetryCount};
            try
            {
                string detail;
                decimal value;
                if (Input.CommunicationType == "BacnetIp")
                {
                    var result = await bacnet.ReadPresentValueAsync(config, meter, HttpContext.RequestAborted);
                    value = result.Value;
                    detail = $"BACnet/IP device {Input.BacnetDeviceInstance}, object {result.ObjectIdentifier}, Present_Value {result.RawValue}";
                }
                else
                {
                    var result=await modbus.ReadHoldingRegistersAsync(config,meter.StartingRegister,ModbusValueConverter.RegisterCount(meter.DataType),HttpContext.RequestAborted);
                    value=ModbusValueConverter.ConvertValue(result.Registers,meter.DataType,meter.WordOrder,meter.ScalingFactor);
                    detail=$"registers [{string.Join(", ",result.Registers)}], request {result.RequestHex}; response {result.ResponseHex}";
                }
                TestResult=$"PASS - device replied. Meter {meter.Name}, {detail}, converted value {value:N3} {meter.Unit}.";
                if(Input.Id>0){var saved=await db.Controllers.FindAsync(Input.Id);if(saved is not null){saved.Condition="Connected";saved.LastSuccess=DateTimeOffset.UtcNow;saved.Notes=$"Last test: {detail}";await db.SaveChangesAsync();}}
            }
            catch(Exception ex){TestResult=$"FAILED - {ex.Message}";if(Input.Id>0){var saved=await db.Controllers.FindAsync(Input.Id);if(saved is not null){saved.Condition="Failed";saved.Notes=ex.Message;await db.SaveChangesAsync();}}}
        }
        return Page();
    }

    private void ValidateTransport()
    {
        if(Input.CommunicationType is not ("ModbusRtu" or "ModbusTcp" or "BacnetIp")){ModelState.AddModelError("Input.CommunicationType","Select Modbus RTU, Modbus TCP/IP, or BACnet/IP.");return;}
        if(Input.CommunicationType=="ModbusRtu"&&!System.Text.RegularExpressions.Regex.IsMatch(Input.ComPort??"","^COM[0-9]{1,3}$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))ModelState.AddModelError("Input.ComPort","Use a Windows port such as COM3.");
        if(Input.CommunicationType=="ModbusTcp"&&(string.IsNullOrWhiteSpace(Input.IpAddress)||Uri.CheckHostName((Input.IpAddress??"").Trim())==UriHostNameType.Unknown))ModelState.AddModelError("Input.IpAddress","Enter a valid controller IP address or host name.");
        if(Input.CommunicationType=="BacnetIp")
        {
            if(string.IsNullOrWhiteSpace(Input.IpAddress)||Uri.CheckHostName((Input.IpAddress??"").Trim())==UriHostNameType.Unknown)ModelState.AddModelError("Input.IpAddress","Enter a valid BACnet device IP address or host name.");
            try { BacnetNetwork.Validate(BacnetConfig()); } catch (InvalidOperationException ex) { ModelState.AddModelError("Input.BacnetLocalIp", ex.Message); }
        }
    }
}
