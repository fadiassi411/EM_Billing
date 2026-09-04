using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;
using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Services;

public sealed class InvoiceEmailService(ApplicationDbContext db, InvoicePdfService pdf, IDataProtectionProvider protection)
{
    private readonly IDataProtector passwordProtector = protection.CreateProtector("WatchDogEM.SmtpPassword.v1");

    public string ProtectPassword(string password) => passwordProtector.Protect(password);

    public string UnprotectPassword(string protectedPassword) =>
        string.IsNullOrWhiteSpace(protectedPassword) ? "" : passwordProtector.Unprotect(protectedPassword);

    public async Task SendInvoiceAsync(Invoice invoice, string recipient)
    {
        var settings = await db.SmtpConfigurations.AsNoTracking().SingleOrDefaultAsync();
        if (settings is null || !settings.Enabled) throw new InvalidOperationException("SMTP invoice email is not enabled.");
        ValidateRecipient(recipient);

        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromEmail, settings.FromName),
            Subject = $"Energy invoice {invoice.InvoiceNumber} - {invoice.Shop?.Name}",
            Body = BuildBody(invoice),
            IsBodyHtml = true
        };
        message.To.Add(recipient.Trim());
        var pdfBytes = pdf.Generate(invoice);
        message.Attachments.Add(new Attachment(new MemoryStream(pdfBytes), $"{invoice.InvoiceNumber}.pdf", "application/pdf"));

        using var client = BuildClient(settings);
        await client.SendMailAsync(message);
    }

    public async Task SendTestAsync(SmtpConfiguration settings, string password, string recipient)
    {
        ValidateRecipient(recipient);
        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromEmail, settings.FromName),
            Subject = "Watch Dog EM SMTP test",
            Body = "Watch Dog EM connected to this SMTP server successfully.",
            IsBodyHtml = false
        };
        message.To.Add(recipient.Trim());
        using var client = BuildClient(settings, password);
        await client.SendMailAsync(message);
    }

    private SmtpClient BuildClient(SmtpConfiguration settings, string? password = null)
    {
        var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Timeout = 30000
        };
        var secret = password ?? UnprotectPassword(settings.ProtectedPassword);
        if (!string.IsNullOrWhiteSpace(settings.Username)) client.Credentials = new NetworkCredential(settings.Username, secret);
        return client;
    }

    private static string BuildBody(Invoice invoice)
    {
        var shop = HtmlEncoder.Default.Encode(invoice.Shop?.Name ?? "Customer");
        var number = HtmlEncoder.Default.Encode(invoice.InvoiceNumber);
        var currency = HtmlEncoder.Default.Encode(invoice.Currency);
        return $"<p>Dear {shop},</p><p>Your energy invoice <strong>{number}</strong> is attached as a PDF.</p>" +
               $"<p>Total due: <strong>{currency} {invoice.Total:N2}</strong><br>Due date: {invoice.DueDate:dd MMM yyyy}</p>" +
               "<p>Regards,<br>Watch Dog EM</p>";
    }

    private static void ValidateRecipient(string recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient)) throw new InvalidOperationException("Enter a recipient email address.");
        try { _ = new MailAddress(recipient.Trim()); }
        catch (FormatException) { throw new InvalidOperationException("Enter a valid recipient email address."); }
    }
}
