using MallEnergyBilling.Web.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace MallEnergyBilling.Web.Data;
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
 public DbSet<Controller> Controllers=>Set<Controller>(); public DbSet<Shop> Shops=>Set<Shop>(); public DbSet<Meter> Meters=>Set<Meter>(); public DbSet<MeterReading> MeterReadings=>Set<MeterReading>(); public DbSet<Tariff> Tariffs=>Set<Tariff>(); public DbSet<BillingPeriod> BillingPeriods=>Set<BillingPeriod>(); public DbSet<InvoiceSchedule> InvoiceSchedules=>Set<InvoiceSchedule>(); public DbSet<Invoice> Invoices=>Set<Invoice>(); public DbSet<Payment> Payments=>Set<Payment>(); public DbSet<AuditLog> AuditLogs=>Set<AuditLog>(); public DbSet<SmtpConfiguration> SmtpConfigurations=>Set<SmtpConfiguration>();
 protected override void OnModelCreating(ModelBuilder b){base.OnModelCreating(b);b.Entity<Shop>().HasIndex(x=>x.ShopNumber).IsUnique();b.Entity<Meter>().HasIndex(x=>x.SerialNumber).IsUnique();b.Entity<Invoice>().HasIndex(x=>x.InvoiceNumber).IsUnique();b.Entity<Invoice>().HasIndex(x=>new{x.MeterId,x.BillingPeriodId}).IsUnique();b.Entity<Tariff>().HasIndex(x=>new{x.MeterId,x.EffectiveFrom});}
}
