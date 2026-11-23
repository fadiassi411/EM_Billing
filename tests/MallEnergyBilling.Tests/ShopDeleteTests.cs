using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShopsIndex = MallEnergyBilling.Web.Pages.Admin.Shops.IndexModel;

namespace MallEnergyBilling.Tests;

public sealed class ShopDeleteTests : IAsyncLifetime
{
    readonly SqliteConnection connection = new("Data Source=:memory:");
    ApplicationDbContext db = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }

    sealed class NoTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    ShopsIndex Page()
    {
        var http = new DefaultHttpContext();
        return new ShopsIndex(db) { PageContext = new PageContext { HttpContext = http }, TempData = new TempDataDictionary(http, new NoTempData()) };
    }

    async Task<Shop> AddShop(string number)
    {
        var shop = new Shop { ShopNumber = number, Name = number };
        db.Shops.Add(shop); await db.SaveChangesAsync();
        return shop;
    }

    async Task<Meter> AddMeter(Shop shop)
    {
        var controller = new Controller { Name = "C-" + shop.ShopNumber };
        db.Controllers.Add(controller); await db.SaveChangesAsync();
        var meter = new Meter { Name = "M-" + shop.ShopNumber, SerialNumber = "S-" + shop.ShopNumber, ShopId = shop.Id, ControllerId = controller.Id };
        db.Meters.Add(meter); await db.SaveChangesAsync();
        return meter;
    }

    [Fact] public async Task EmptyShopIsDeletedAndAudited()
    {
        var shop = await AddShop("SHOP-09");
        var page = Page();
        await page.OnPostDeleteAsync(shop.Id);
        db.ChangeTracker.Clear();
        Assert.False(await db.Shops.AnyAsync(x => x.Id == shop.Id));
        Assert.True(await db.AuditLogs.AnyAsync(x => x.Action == "Shop deleted" && x.EntityId == shop.Id.ToString()));
        Assert.Equal("Shop SHOP-09 deleted.", page.TempData["Message"]);
    }

    [Fact] public async Task ShopWithMetersIsKept()
    {
        var shop = await AddShop("SHOP-10");
        var meter = await AddMeter(shop);
        var page = Page();
        await page.OnPostDeleteAsync(shop.Id);
        db.ChangeTracker.Clear();
        Assert.True(await db.Shops.AnyAsync(x => x.Id == shop.Id));
        Assert.True(await db.Meters.AnyAsync(x => x.Id == meter.Id)); // nothing cascaded away
        Assert.Contains("1 meter", (string)page.TempData["Error"]!);
    }

    [Fact] public async Task ShopWithInvoiceHistoryIsKept()
    {
        var shop = await AddShop("SHOP-11");
        var other = await AddShop("SHOP-12");
        var meter = await AddMeter(other); // the meter has since moved to another shop
        var period = new BillingPeriod { Number = "BP-1", StartDate = new(2026, 10, 1), EndDate = new(2026, 10, 31) };
        db.BillingPeriods.Add(period); await db.SaveChangesAsync();
        db.Invoices.Add(new Invoice { InvoiceNumber = "INV-1", BillingPeriodId = period.Id, MeterId = meter.Id, ShopId = shop.Id });
        await db.SaveChangesAsync();
        var page = Page();
        await page.OnPostDeleteAsync(shop.Id);
        db.ChangeTracker.Clear();
        Assert.True(await db.Shops.AnyAsync(x => x.Id == shop.Id));
        Assert.Equal(1, await db.Invoices.CountAsync());
        Assert.Contains("Inactive", (string)page.TempData["Error"]!);
    }

    [Fact] public async Task DeletingTwiceShowsAMessageNotAnError()
    {
        var page = Page();
        await page.OnPostDeleteAsync(999);
        Assert.Contains("no longer exists", (string)page.TempData["Error"]!);
    }
}

public sealed class ChannelGenerateTests : IAsyncLifetime
{
    readonly SqliteConnection connection = new("Data Source=:memory:");
    ApplicationDbContext db = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }

    [Fact] public async Task ASerialAlreadyUsedByAnotherMeterGivesAMessageNotAnErrorPage()
    {
        var shop = new Shop { ShopNumber = "SHOP-10", Name = "Shop No. 10" };
        var controller = new Controller { Name = "PLC", CommunicationType = "ModbusTcp" };
        db.Meters.Add(new Meter { Name = "Shop No. 10", SerialNumber = "1235-356-ER6600", Shop = shop, Controller = controller, StartingRegister = 4510 });
        await db.SaveChangesAsync();
        var http = new DefaultHttpContext();
        var page = new MallEnergyBilling.Web.Pages.Admin.Controllers.ChannelsModel(db)
        {
            PageContext = new PageContext { HttpContext = http }, TempData = new TempDataDictionary(http, new NoTempDataProvider()),
            ShopId = shop.Id, FirstRegister = 4518, RegisterStride = 8, ChannelCount = 3, FirstMeterSerial = " 1235-356-ER6600 "
        };
        var result = await page.OnPostGenerateAsync(controller.Id);
        Assert.IsType<PageResult>(result);
        Assert.Contains("already used by meter Shop No. 10", page.ModelState[nameof(page.FirstMeterSerial)]!.Errors.Single().ErrorMessage);
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Meters.CountAsync());
    }

    [Fact] public async Task MetersCanBeCreatedWithoutASerialButAnEnteredSerialStaysUnique()
    {
        var shop = new Shop { ShopNumber = "SHOP-10", Name = "Shop No. 10" };
        var controller = new Controller { Name = "PLC", CommunicationType = "ModbusTcp" };
        db.Meters.Add(new Meter { Name = "Shop No. 10", SerialNumber = "1235-356-ER6600", Shop = shop, Controller = controller, StartingRegister = 4510 });
        await db.SaveChangesAsync();
        var http = new DefaultHttpContext();
        var page = new MallEnergyBilling.Web.Pages.Admin.Controllers.ChannelsModel(db)
        {
            PageContext = new PageContext { HttpContext = http }, TempData = new TempDataDictionary(http, new NoTempDataProvider()),
            ShopId = shop.Id, FirstRegister = 4518, RegisterStride = 8, ChannelCount = 3, DataType = RegisterDataType.Float32, ScalingFactor = 1
        };
        await page.OnPostGenerateAsync(controller.Id);
        db.ChangeTracker.Clear();
        var created = await db.Meters.Where(x => x.StartingRegister != 4510).OrderBy(x => x.StartingRegister).ToListAsync();
        Assert.Equal(new[] { 4518, 4526, 4534 }, created.Select(x => x.StartingRegister));
        Assert.All(created, x => Assert.Equal("", x.SerialNumber));

        db.Meters.Add(new Meter { Name = "Copy", SerialNumber = "1235-356-ER6600", ShopId = shop.Id, ControllerId = controller.Id, StartingRegister = 4542 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    sealed class NoTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
