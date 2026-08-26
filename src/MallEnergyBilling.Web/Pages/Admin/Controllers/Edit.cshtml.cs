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
public sealed class EditModel(ApplicationDbContext db, IModbusRtuService modbus) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public List<Meter> Meters { get; private set; } = [];
    public string? TestResult { get; private set; }
    public sealed class InputModel
    {
        public int Id { get; set; }
        [Required, StringLength(80)] public string Name { get; set; } = "";
        [Required, StringLength(40)] public string MdbPanel { get; set; } = "";
        [Required] public string CommunicationType { get; set; } = "ModbusRtu";
        [Required, RegularExpression("^COM[0-9]{1,3}$", ErrorMessage="Use a Windows port such as COM3.")] public string ComPort { get; set; } = "COM1";
        [Range(300, 921600)] public int BaudRate { get; set; } = 9600;
        [Required] public string Parity { get; set; } = "Even";
        [Range(7,8)] public int DataBits { get; set; } = 8;
        [Range(1,2)] public int StopBits { get; set; } = 1;
        [Range(1,247)] public byte SlaveAddress { get; set; } = 1;
        [Range(1,3600)] public int PollingIntervalSeconds { get; set; } = 5;
        [Range(100,30000)] public int TimeoutMilliseconds { get; set; } = 1000;
        [Range(0,10)] public int RetryCount { get; set; } = 2;
        public bool Enabled { get; set; } = true;
        public string Notes { get; set; } = "";
        public bool ConfirmRealMode { get; set; }
        public string ChangeReason { get; set; } = "";
    }
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null) return Page();
        var x = await db.Controllers.FindAsync(id.Value); if (x is null) return NotFound();
        Input = new(){Id=x.Id,Name=x.Name,MdbPanel=x.MdbPanel,CommunicationType=x.CommunicationType,ComPort=x.ComPort,BaudRate=x.BaudRate,Parity=x.Parity,DataBits=x.DataBits,StopBits=x.StopBits,SlaveAddress=x.SlaveAddress,PollingIntervalSeconds=x.PollingIntervalSeconds,TimeoutMilliseconds=x.TimeoutMilliseconds,RetryCount=x.RetryCount,Enabled=x.Enabled,Notes=x.Notes};
        Meters = await db.Meters.Where(m=>m.ControllerId==x.Id).ToListAsync(); return Page();
    }
    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!Input.ConfirmRealMode) ModelState.AddModelError("Input.ConfirmRealMode","Confirm that the controller settings are ready for real meter communication.");
        if(!ModelState.IsValid){if(Input.Id>0)Meters=await db.Meters.Where(m=>m.ControllerId==Input.Id).ToListAsync();return Page();}
        ControllerModel x; string old;
        if(Input.Id==0){x=new();db.Controllers.Add(x);old="New controller";}else{x=await db.Controllers.FindAsync(Input.Id)??throw new InvalidOperationException("Controller not found.");old=JsonSerializer.Serialize(x);}
        x.Name=Input.Name.Trim();x.MdbPanel=Input.MdbPanel.Trim();x.CommunicationType="ModbusRtu";x.ComPort=Input.ComPort.ToUpperInvariant();x.BaudRate=Input.BaudRate;x.Parity=Input.Parity;x.DataBits=Input.DataBits;x.StopBits=Input.StopBits;x.SlaveAddress=Input.SlaveAddress;x.PollingIntervalSeconds=Input.PollingIntervalSeconds;x.TimeoutMilliseconds=Input.TimeoutMilliseconds;x.RetryCount=Input.RetryCount;x.Enabled=Input.Enabled;x.Notes=Input.Notes;x.Condition="Ready to test";
        db.AuditLogs.Add(new(){Timestamp=DateTimeOffset.UtcNow,UserId=User.Identity?.Name??"Administrator",Action=Input.Id==0?"Controller created":"Controller configuration changed",EntityType="Controller",EntityId=Input.Id.ToString(),OldValue=old,NewValue=JsonSerializer.Serialize(Input),Reason=string.IsNullOrWhiteSpace(Input.ChangeReason)?"Not provided":Input.ChangeReason.Trim(),SourceIp=HttpContext.Connection.RemoteIpAddress?.ToString()??""});await db.SaveChangesAsync();return RedirectToPage("Index");
    }
    public async Task<IActionResult> OnPostTestAsync()
    {
        if(Input.Id>0)Meters=await db.Meters.Where(m=>m.ControllerId==Input.Id).OrderBy(m=>m.StartingRegister).ToListAsync();
        if(Meters.Count==0)TestResult="FAILED - add at least one meter register channel before testing.";
        else
        {
            var meter=Meters[0];var config=new ControllerModel{ComPort=Input.ComPort.ToUpperInvariant(),BaudRate=Input.BaudRate,Parity=Input.Parity,DataBits=Input.DataBits,StopBits=Input.StopBits,SlaveAddress=Input.SlaveAddress,TimeoutMilliseconds=Input.TimeoutMilliseconds,RetryCount=Input.RetryCount};
            try{var result=await modbus.ReadHoldingRegistersAsync(config,meter.StartingRegister,ModbusValueConverter.RegisterCount(meter.DataType),HttpContext.RequestAborted);var value=ModbusValueConverter.ConvertValue(result.Registers,meter.DataType,meter.WordOrder,meter.ScalingFactor);TestResult=$"PASS - PLC replied. Meter {meter.Name}, registers [{string.Join(", ",result.Registers)}], converted value {value:N3} kWh. Request {result.RequestHex}; response {result.ResponseHex}.";if(Input.Id>0){var saved=await db.Controllers.FindAsync(Input.Id);if(saved is not null){saved.Condition="Connected";saved.LastSuccess=DateTimeOffset.UtcNow;saved.Notes=$"Last response {result.ResponseHex}";await db.SaveChangesAsync();}}}
            catch(Exception ex){TestResult=$"FAILED - {ex.Message}";if(Input.Id>0){var saved=await db.Controllers.FindAsync(Input.Id);if(saved is not null){saved.Condition="Failed";saved.Notes=ex.Message;await db.SaveChangesAsync();}}}
        }
        return Page();
    }
}
