using System.ComponentModel.DataAnnotations;
using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages.Admin.Email;

public sealed class IndexModel(ApplicationDbContext db, InvoiceEmailService email) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public bool PasswordSaved { get; private set; }

    public sealed class InputModel
    {
        public bool Enabled { get; set; }
        public bool AutoSendPublishedInvoices { get; set; }
        public string Host { get; set; } = "";
        [Range(1, 65535)] public int Port { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public string Username { get; set; } = "";
        [DataType(DataType.Password)] public string Password { get; set; } = "";
        [EmailAddress] public string FromEmail { get; set; } = "";
        public string FromName { get; set; } = "Watch Dog EM";
        [EmailAddress] public string TestRecipient { get; set; } = "";
    }

    public async Task OnGetAsync()
    {
        var saved = await db.SmtpConfigurations.AsNoTracking().SingleOrDefaultAsync();
        if (saved is null) return;
        Input = new() { Enabled=saved.Enabled, AutoSendPublishedInvoices=saved.AutoSendPublishedInvoices, Host=saved.Host, Port=saved.Port, EnableSsl=saved.EnableSsl, Username=saved.Username, FromEmail=saved.FromEmail, FromName=saved.FromName, TestRecipient=saved.FromEmail };
        PasswordSaved = !string.IsNullOrWhiteSpace(saved.ProtectedPassword);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var saved = await db.SmtpConfigurations.SingleOrDefaultAsync();
        PasswordSaved = !string.IsNullOrWhiteSpace(saved?.ProtectedPassword);
        if (Input.Enabled) ValidateConfiguration(requireRecipient: false, PasswordSaved);
        if (!ModelState.IsValid) return Page();

        saved ??= new SmtpConfiguration { Id = 1 };
        if (db.Entry(saved).State == EntityState.Detached) db.SmtpConfigurations.Add(saved);
        saved.Enabled=Input.Enabled;saved.AutoSendPublishedInvoices=Input.Enabled&&Input.AutoSendPublishedInvoices;saved.Host=Input.Host.Trim();saved.Port=Input.Port;saved.EnableSsl=Input.EnableSsl;saved.Username=Input.Username.Trim();saved.FromEmail=Input.FromEmail.Trim();saved.FromName=string.IsNullOrWhiteSpace(Input.FromName)?"Watch Dog EM":Input.FromName.Trim();saved.UpdatedAt=DateTimeOffset.UtcNow;saved.UpdatedBy=User.Identity?.Name??"Administrator";
        if (!string.IsNullOrWhiteSpace(Input.Password)) saved.ProtectedPassword=email.ProtectPassword(Input.Password);
        if (!saved.AutoSendPublishedInvoices)
        {
            var pending = await db.Invoices.Where(x => x.AutoEmailRequested && x.EmailSentAt == null).ToListAsync();
            foreach (var invoice in pending) invoice.AutoEmailRequested = false;
        }
        db.AuditLogs.Add(new(){Timestamp=DateTimeOffset.UtcNow,UserId=saved.UpdatedBy,Action="SMTP configuration changed",EntityType="SmtpConfiguration",EntityId="1",NewValue=$"Enabled={saved.Enabled}; Automatic={saved.AutoSendPublishedInvoices}; Host={saved.Host}; Port={saved.Port}; SSL={saved.EnableSsl}; From={saved.FromEmail}",Reason="Administrator email configuration",SourceIp=HttpContext.Connection.RemoteIpAddress?.ToString()??""});
        await db.SaveChangesAsync();
        TempData["EmailSuccess"] = saved.AutoSendPublishedInvoices ? "Automatic PDF invoice email is enabled for all customers." : saved.Enabled ? "SMTP email is enabled for manual sending only." : "All invoice email is disabled.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync()
    {
        var saved = await db.SmtpConfigurations.AsNoTracking().SingleOrDefaultAsync();
        PasswordSaved = !string.IsNullOrWhiteSpace(saved?.ProtectedPassword);
        ValidateConfiguration(requireRecipient: true, PasswordSaved);
        if (!ModelState.IsValid) return Page();
        var candidate = new SmtpConfiguration { Host=Input.Host.Trim(),Port=Input.Port,EnableSsl=Input.EnableSsl,Username=Input.Username.Trim(),FromEmail=Input.FromEmail.Trim(),FromName=string.IsNullOrWhiteSpace(Input.FromName)?"Watch Dog EM":Input.FromName.Trim() };
        var password = string.IsNullOrWhiteSpace(Input.Password) && saved is not null ? email.UnprotectPassword(saved.ProtectedPassword) : Input.Password;
        try
        {
            await email.SendTestAsync(candidate, password, Input.TestRecipient);
            TempData["EmailSuccess"] = $"Test email sent to {Input.TestRecipient.Trim()}. Save the settings to activate invoice email.";
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"SMTP test failed: {ex.Message}");
            return Page();
        }
        return Page();
    }

    private void ValidateConfiguration(bool requireRecipient, bool passwordSaved)
    {
        if (string.IsNullOrWhiteSpace(Input.Host)) ModelState.AddModelError("Input.Host", "SMTP server is required.");
        if (string.IsNullOrWhiteSpace(Input.FromEmail)) ModelState.AddModelError("Input.FromEmail", "Sender email is required.");
        if (!string.IsNullOrWhiteSpace(Input.Username) && string.IsNullOrWhiteSpace(Input.Password) && !passwordSaved) ModelState.AddModelError("Input.Password", "SMTP password is required for this username.");
        if (requireRecipient && string.IsNullOrWhiteSpace(Input.TestRecipient)) ModelState.AddModelError("Input.TestRecipient", "Enter a test recipient.");
    }
}
