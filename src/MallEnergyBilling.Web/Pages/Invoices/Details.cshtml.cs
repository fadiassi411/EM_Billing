using System.ComponentModel.DataAnnotations;
using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages.Invoices;

[Authorize]
public sealed class DetailsModel(ApplicationDbContext db, InvoicePdfService pdf, InvoiceEmailService email) : PageModel
{
    public Invoice Invoice { get; private set; } = null!;
    public bool EmailEnabled { get; private set; }
    public bool CanSendEmail => User.IsInRole("Administrator") || User.IsInRole("BillingManager");
    [BindProperty, EmailAddress] public string RecipientEmail { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var invoice = await Load(id);
        if (invoice is null) return NotFound();
        Invoice = invoice;
        RecipientEmail = invoice.Shop?.Email ?? "";
        EmailEnabled = await db.SmtpConfigurations.AnyAsync(x => x.Enabled);
        return Page();
    }

    public async Task<IActionResult> OnGetDownloadAsync(int id)
    {
        var invoice = await Load(id);
        if (invoice is null) return NotFound();
        return File(pdf.Generate(invoice), "application/pdf", $"{invoice.InvoiceNumber}.pdf");
    }

    public async Task<IActionResult> OnPostSendEmailAsync(int id)
    {
        if (!CanSendEmail) return Forbid();
        var invoice = await Load(id);
        if (invoice is null) return NotFound();
        if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Cancelled)
        {
            TempData["InvoiceEmailError"] = "Only published invoices can be emailed.";
            return RedirectToPage(new { id });
        }
        try
        {
            await email.SendInvoiceAsync(invoice, RecipientEmail);
            invoice.EmailSentAt = DateTimeOffset.UtcNow;
            invoice.EmailLastAttemptAt = invoice.EmailSentAt;
            invoice.EmailAttemptCount++;
            invoice.EmailRecipient = RecipientEmail.Trim();
            invoice.EmailDeliveryError = "";
            db.AuditLogs.Add(Audit(invoice, "Invoice PDF emailed", RecipientEmail.Trim(), "SMTP delivery accepted"));
            await db.SaveChangesAsync();
            TempData["InvoiceEmailSuccess"] = $"Invoice {invoice.InvoiceNumber} was emailed to {RecipientEmail.Trim()}.";
        }
        catch (Exception ex)
        {
            db.AuditLogs.Add(Audit(invoice, "Invoice email failed", RecipientEmail?.Trim() ?? "", ex.Message));
            await db.SaveChangesAsync();
            TempData["InvoiceEmailError"] = $"Email was not sent: {ex.Message}";
        }
        return RedirectToPage(new { id });
    }

    private AuditLog Audit(Invoice invoice, string action, string recipient, string result) => new()
    {
        Timestamp=DateTimeOffset.UtcNow,UserId=User.Identity?.Name??"Administrator",Action=action,EntityType="Invoice",EntityId=invoice.Id.ToString(),NewValue=$"Recipient={recipient}; {result}",Reason="Customer invoice delivery",SourceIp=HttpContext.Connection.RemoteIpAddress?.ToString()??""
    };

    private async Task<Invoice?> Load(int id) => await db.Invoices.Include(x=>x.Shop).Include(x=>x.Meter).ThenInclude(x=>x!.Controller).Include(x=>x.BillingPeriod).Include(x=>x.Payments).FirstOrDefaultAsync(x=>x.Id==id);
}
