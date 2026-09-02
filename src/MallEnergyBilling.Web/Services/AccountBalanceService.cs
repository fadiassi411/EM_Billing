using MallEnergyBilling.Web.Models;

namespace MallEnergyBilling.Web.Services;

public static class AccountBalanceService
{
    public static decimal Charges(Invoice invoice) => invoice.Total - invoice.PreviousBalance;
    public static decimal Outstanding(IEnumerable<Invoice> invoices) => decimal.Round(invoices
        .Where(x => x.Status != InvoiceStatus.Cancelled)
        .Sum(x => Charges(x) - x.Payments.Sum(p => p.Amount)), 2, MidpointRounding.AwayFromZero);
    public static InvoiceStatus StatusAfterPayment(Invoice invoice, DateTimeOffset now)
    {
        if (invoice.Status is InvoiceStatus.Cancelled or InvoiceStatus.Draft) return invoice.Status;
        if (invoice.PaidAmount >= invoice.Total) return InvoiceStatus.Paid;
        if (invoice.PaidAmount > 0) return InvoiceStatus.PartiallyPaid;
        return invoice.DueDate < now ? InvoiceStatus.Overdue : InvoiceStatus.Finalized;
    }
}
