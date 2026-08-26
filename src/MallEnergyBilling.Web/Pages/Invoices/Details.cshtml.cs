using MallEnergyBilling.Web.Data;using MallEnergyBilling.Web.Models;using MallEnergyBilling.Web.Services;using Microsoft.AspNetCore.Authorization;using Microsoft.AspNetCore.Mvc;using Microsoft.AspNetCore.Mvc.RazorPages;using Microsoft.EntityFrameworkCore;
namespace MallEnergyBilling.Web.Pages.Invoices;
[Authorize]
public sealed class DetailsModel(ApplicationDbContext db,InvoicePdfService pdf):PageModel
{
 public Invoice Invoice{get;private set;}=null!;
 public async Task<IActionResult>OnGetAsync(int id){var invoice=await Load(id);if(invoice is null)return NotFound();Invoice=invoice;return Page();}
 public async Task<IActionResult>OnGetDownloadAsync(int id){var invoice=await Load(id);if(invoice is null)return NotFound();return File(pdf.Generate(invoice),"application/pdf",$"{invoice.InvoiceNumber}.pdf");}
 async Task<Invoice?>Load(int id)=>await db.Invoices.Include(x=>x.Shop).Include(x=>x.Meter).ThenInclude(x=>x!.Controller).Include(x=>x.BillingPeriod).Include(x=>x.Payments).FirstOrDefaultAsync(x=>x.Id==id);
}
