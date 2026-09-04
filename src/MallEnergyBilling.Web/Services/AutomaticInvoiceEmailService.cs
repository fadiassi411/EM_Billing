using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Services;

public sealed class AutomaticInvoiceEmailService(IServiceScopeFactory scopes, ILogger<AutomaticInvoiceEmailService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SendPendingInvoices(stoppingToken); }
            catch (Exception ex) { log.LogError(ex, "Automatic invoice email processing failed."); }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task SendPendingInvoices(CancellationToken token)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var settings = await db.SmtpConfigurations.AsNoTracking().SingleOrDefaultAsync(token);
        if (settings is null || !settings.Enabled || !settings.AutoSendPublishedInvoices) return;

        var retryBefore = DateTimeOffset.UtcNow.AddMinutes(-15);
        var candidates = await db.Invoices
            .Include(x => x.Shop).Include(x => x.Meter).Include(x => x.BillingPeriod).Include(x => x.Payments)
            .Where(x => x.AutoEmailRequested && x.EmailSentAt == null && x.EmailAttemptCount < 5 &&
                        (x.Status == InvoiceStatus.Finalized || x.Status == InvoiceStatus.PartiallyPaid || x.Status == InvoiceStatus.Overdue))
            .OrderBy(x => x.Id).Take(100).ToListAsync(token);
        var invoices = candidates.Where(x => x.EmailLastAttemptAt == null || x.EmailLastAttemptAt < retryBefore).Take(20).ToList();

        var sender = scope.ServiceProvider.GetRequiredService<InvoiceEmailService>();
        foreach (var invoice in invoices)
        {
            var recipient = invoice.Shop?.Email?.Trim() ?? "";
            invoice.EmailLastAttemptAt = DateTimeOffset.UtcNow;
            invoice.EmailAttemptCount++;
            invoice.EmailRecipient = recipient;
            try
            {
                if (string.IsNullOrWhiteSpace(recipient))
                {
                    invoice.EmailAttemptCount = 5;
                    throw new InvalidOperationException("The shop has no customer email address.");
                }
                await sender.SendInvoiceAsync(invoice, recipient);
                invoice.EmailSentAt = DateTimeOffset.UtcNow;
                invoice.EmailDeliveryError = "";
                db.AuditLogs.Add(Audit(invoice, "Automatic invoice email sent", $"Sent to {recipient}"));
            }
            catch (Exception ex)
            {
                invoice.EmailDeliveryError = ex.Message;
                db.AuditLogs.Add(Audit(invoice, "Automatic invoice email failed", $"Recipient={recipient}; attempt={invoice.EmailAttemptCount}; error={ex.Message}"));
                log.LogWarning(ex, "Automatic email failed for invoice {InvoiceNumber} on attempt {Attempt}.", invoice.InvoiceNumber, invoice.EmailAttemptCount);
            }
            await db.SaveChangesAsync(token);
        }
    }

    private static AuditLog Audit(Invoice invoice, string action, string value) => new()
    {
        Timestamp = DateTimeOffset.UtcNow, UserId = "System", Action = action, EntityType = "Invoice",
        EntityId = invoice.Id.ToString(), NewValue = value, Reason = "Automatic period-end invoice delivery", SourceIp = "Background service"
    };
}
