using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;

namespace MallEnergyBilling.Tests;

public sealed class AccountBalanceTests
{
    [Fact]
    public void PreviousBalancesAreNotCountedTwice()
    {
        var first = new Invoice { Total = 100, PreviousBalance = 0, Status = InvoiceStatus.Finalized };
        var second = new Invoice { Total = 150, PreviousBalance = 100, Status = InvoiceStatus.Finalized };
        Assert.Equal(150, AccountBalanceService.Outstanding([first, second]));
    }

    [Fact]
    public void PaymentsReduceOutstandingAndSetStatus()
    {
        var invoice = new Invoice { Total = 100, Status = InvoiceStatus.Finalized, DueDate = DateTimeOffset.Now.AddDays(2), Payments = [new Payment { Amount = 40 }] };
        Assert.Equal(60, AccountBalanceService.Outstanding([invoice]));
        Assert.Equal(InvoiceStatus.PartiallyPaid, AccountBalanceService.StatusAfterPayment(invoice, DateTimeOffset.Now));
        invoice.Payments.Add(new Payment { Amount = 60 });
        Assert.Equal(InvoiceStatus.Paid, AccountBalanceService.StatusAfterPayment(invoice, DateTimeOffset.Now));
    }
}
