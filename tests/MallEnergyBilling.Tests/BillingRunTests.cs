using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Pages.Operations;
using MallEnergyBilling.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MallEnergyBilling.Tests;

public sealed class BillingCycleTests
{
    static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact] public void CalendarMonthCycle()
    {
        Assert.Equal(D(2026, 11, 1), BillingCycle.LatestCycleStart(D(2026, 11, 19), 1));
        Assert.Equal((D(2026, 10, 1), D(2026, 10, 31)), BillingCycle.LastCompletedPeriod(D(2026, 11, 19), 1));
        Assert.Equal((D(2026, 12, 1), D(2026, 12, 31)), BillingCycle.LastCompletedPeriod(D(2027, 1, 5), 1));
    }

    [Fact] public void MidMonthCycleEndsTheDayBeforeTheNextStart()
    {
        Assert.Equal((D(2026, 10, 25), D(2026, 11, 24)), BillingCycle.LastCompletedPeriod(D(2026, 11, 25), 25));
        Assert.Equal((D(2026, 9, 25), D(2026, 10, 24)), BillingCycle.LastCompletedPeriod(D(2026, 11, 24), 25));
        Assert.Equal((D(2026, 12, 15), D(2027, 1, 14)), BillingCycle.LastCompletedPeriod(D(2027, 1, 15), 15));
    }

    [Fact] public void FebruaryWorksForEveryAllowedStartDay()
    {
        for (var day = BillingCycle.MinStartDay; day <= BillingCycle.MaxStartDay; day++)
        {
            var (start, end) = BillingCycle.PeriodEndingBefore(new DateOnly(2027, 3, day));
            Assert.Equal(new DateOnly(2027, 2, day), start);
            Assert.Equal(new DateOnly(2027, 3, day).AddDays(-1), end);
            var next = BillingCycle.PeriodEndingBefore(new DateOnly(2027, 4, day));
            Assert.Equal(end.AddDays(1), next.Start); // no gap, no overlap
        }
    }

    [Fact] public void FirstRunIsNeverBeforeTheFirstCompletedPeriod()
    {
        Assert.Equal(D(2026, 12, 1), BillingCycle.FirstRunOnOrAfter(D(2026, 11, 19), 1, 0));
        Assert.Equal(D(2026, 11, 22), BillingCycle.FirstRunOnOrAfter(D(2026, 11, 19), 15, 7));
        Assert.Equal(D(2026, 12, 22), BillingCycle.FirstRunOnOrAfter(D(2026, 11, 23), 15, 7));
        Assert.Equal(D(2026, 11, 19), BillingCycle.FirstRunOnOrAfter(D(2026, 11, 19), 19, 0));
    }

    [Fact] public void DescribesThePeriod()
    {
        Assert.Equal("1st to the last day of each month", BillingCycle.Describe(1));
        Assert.Equal("25th to the 24th of the next month", BillingCycle.Describe(25));
        Assert.Equal("22nd to the 21st of the next month", BillingCycle.Describe(22));
        Assert.Equal("12th to the 11th of the next month", BillingCycle.Describe(12));
    }
}

public sealed class BillingRunTests : IAsyncLifetime
{
    readonly SqliteConnection connection = new("Data Source=:memory:");
    readonly FakeTimeProvider clock = new(At(2026, 11, 19, 10));
    ServiceProvider provider = null!;
    int meterId, shopId;

    static DateTimeOffset At(int y, int m, int d, int hour = 0, int minute = 0) => BillingCycle.LocalStart(new DateOnly(y, m, d)).AddHours(hour).AddMinutes(minute);
    static DateOnly D(int y, int m, int d) => new(y, m, d);

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        provider = new ServiceCollection()
            .AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connection))
            .AddSingleton<BillingCalculator>().AddSingleton<TariffResolver>().AddSingleton<TimeProvider>(clock)
            .AddScoped<BillingRunService>().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = Db(scope);
        await db.Database.EnsureCreatedAsync();
        var shop = new Shop { ShopNumber = "A1", Name = "Shop A" };
        var meter = new Meter { Name = "M1", SerialNumber = "S1", Shop = shop, Controller = new Controller { Name = "RTU", CommunicationType = "ModbusRtu" }, Active = true };
        db.Meters.Add(meter);
        await db.SaveChangesAsync();
        (meterId, shopId) = (meter.Id, shop.Id);
        db.Tariffs.Add(new Tariff { MeterId = meterId, PricePerKwh = .5m, Currency = "USD", EffectiveFrom = At(2026, 8, 1) });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() { await provider.DisposeAsync(); await connection.DisposeAsync(); }

    static ApplicationDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    async Task Readings(params (DateTimeOffset At, decimal Value, string Quality, bool Review)[] rows)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = Db(scope);
        foreach (var r in rows) db.MeterReadings.Add(new MeterReading { MeterId = meterId, AccumulatedKwh = r.Value, Timestamp = r.At, Quality = r.Quality, RequiresReview = r.Review });
        await db.SaveChangesAsync();
    }
    static (DateTimeOffset, decimal, string, bool) Good(DateTimeOffset at, decimal value) => (at, value, "Good", false);
    static (DateTimeOffset, decimal, string, bool) Review(DateTimeOffset at, decimal value) => (at, value, "Review: counter decrease or negative value", true);

    async Task<BillingRunOutcome> Run(DateOnly start, DateOnly end)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BillingRunService>().GenerateAsync(new BillingRunRequest(start, end, end.AddDays(1), end.AddDays(11)));
    }

    async Task<List<Invoice>> Invoices()
    {
        await using var scope = provider.CreateAsyncScope();
        return await Db(scope).Invoices.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
    }

    [Fact] public async Task BillsACompletedMonth()
    {
        await Readings(Good(At(2026, 9, 30, 23), 1000), Good(At(2026, 10, 15), 1100), Good(At(2026, 10, 31, 23, 45), 1250), Good(At(2026, 11, 1, 0, 10), 1251));
        var outcome = await Run(D(2026, 10, 1), D(2026, 10, 31));
        Assert.True(outcome.Created, outcome.Message);
        var invoice = Assert.Single(await Invoices());
        Assert.Equal(1000, invoice.OpeningReading); Assert.Equal(1250, invoice.ClosingReading);
        Assert.Equal(250, invoice.ConsumptionKwh); Assert.Equal(125, invoice.Total);
        Assert.Equal(InvoiceStatus.Finalized, invoice.Status); Assert.True(invoice.Locked);
    }

    [Fact] public async Task UnresolvedCounterDecreaseIsNeverBilled()
    {
        await Readings(Good(At(2026, 9, 30, 23), 1000), Good(At(2026, 10, 20), 1100), Review(At(2026, 10, 31, 23), 40));
        var outcome = await Run(D(2026, 10, 1), D(2026, 10, 31));
        Assert.False(outcome.Created);
        Assert.Contains("needs review", outcome.Message);
        Assert.Empty(await Invoices());
    }

    [Fact] public async Task TransientGlitchThatRecoveredIsIgnored()
    {
        await Readings(Good(At(2026, 9, 30, 23), 1000), Review(At(2026, 10, 15), 5), Good(At(2026, 10, 15, 0, 15), 1120), Good(At(2026, 10, 31, 23), 1200));
        var outcome = await Run(D(2026, 10, 1), D(2026, 10, 31));
        Assert.True(outcome.Created, outcome.Message);
        Assert.Equal(200, Assert.Single(await Invoices()).ConsumptionKwh);
    }

    [Fact] public async Task RejectedReadingIsExcludedFromBilling()
    {
        await Readings(Good(At(2026, 9, 30, 23), 1000), Good(At(2026, 10, 30), 1150), (At(2026, 10, 31, 22), 3, BillingRunService.RejectedQuality, false));
        var outcome = await Run(D(2026, 10, 1), D(2026, 10, 31));
        Assert.True(outcome.Created, outcome.Message);
        Assert.Equal(150, Assert.Single(await Invoices()).ConsumptionKwh);
    }

    [Fact] public async Task MeterResetIsBilledAcrossTheNewStartingValue()
    {
        await Readings(Good(At(2026, 9, 30, 23), 1000), Good(At(2026, 10, 10), 1100), (At(2026, 10, 12), 0, BillingRunService.BaselineQuality, false), Good(At(2026, 10, 31, 23), 50));
        var outcome = await Run(D(2026, 10, 1), D(2026, 10, 31));
        Assert.True(outcome.Created, outcome.Message);
        var invoice = Assert.Single(await Invoices());
        Assert.Equal(150, invoice.ConsumptionKwh);
        Assert.Contains("reset", invoice.Notes);
    }

    [Fact] public async Task UnfinishedPeriodIsRejectedButCanBePreviewed()
    {
        await Readings(Good(At(2026, 10, 31, 23), 1000), Good(At(2026, 11, 18), 1100));
        var outcome = await Run(D(2026, 11, 1), D(2026, 11, 30));
        Assert.False(outcome.Created);
        Assert.Contains("only be generated after", outcome.Message);
        await using var scope = provider.CreateAsyncScope();
        var preview = await scope.ServiceProvider.GetRequiredService<BillingRunService>()
            .PreviewAsync(new BillingRunRequest(D(2026, 11, 1), D(2026, 11, 30), D(2026, 12, 1), D(2026, 12, 11)), allowUnfinishedPeriod: true);
        Assert.Empty(preview.Errors);
        Assert.Contains(preview.Warnings, w => w.Contains("unfinished"));
        Assert.Equal(100, Assert.Single(preview.Lines).Result.Consumption);
        Assert.Empty(await Invoices());
    }

    [Fact] public async Task OverlappingPeriodsAreRejectedAndExactRepeatsSkipped()
    {
        await Readings(Good(At(2026, 9, 30, 23), 1000), Good(At(2026, 10, 31, 23), 1100), Good(At(2026, 11, 14, 23), 1200));
        Assert.True((await Run(D(2026, 10, 1), D(2026, 10, 31))).Created);
        var overlap = await Run(D(2026, 10, 15), D(2026, 11, 14));
        Assert.False(overlap.Created); Assert.False(overlap.AlreadyExists);
        Assert.Contains("overlaps", overlap.Message);
        var repeat = await Run(D(2026, 10, 1), D(2026, 10, 31));
        Assert.True(repeat.AlreadyExists);
        Assert.Single(await Invoices());
    }

    [Fact] public async Task TariffSetAfterThePeriodDoesNotRepriceIt()
    {
        await Readings(Good(At(2026, 9, 30, 23), 1000), Good(At(2026, 10, 31, 23), 1100));
        await using (var scope = provider.CreateAsyncScope())
        {
            Db(scope).Tariffs.Add(new Tariff { MeterId = meterId, PricePerKwh = .9m, Currency = "USD", EffectiveFrom = At(2026, 11, 1, 8) });
            await Db(scope).SaveChangesAsync();
        }
        Assert.True((await Run(D(2026, 10, 1), D(2026, 10, 31))).Created);
        Assert.Equal(.5m, Assert.Single(await Invoices()).TariffPerKwh);
    }

    [Fact] public async Task ArrearsStayWithTheShopWhenAMeterMoves()
    {
        await Readings(Good(At(2026, 8, 31, 23), 900), Good(At(2026, 9, 30, 23), 1000), Good(At(2026, 10, 31, 23), 1100));
        Assert.True((await Run(D(2026, 9, 1), D(2026, 9, 30))).Created);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = Db(scope);
            var other = new Shop { ShopNumber = "B1", Name = "Shop B" };
            db.Shops.Add(other); await db.SaveChangesAsync();
            (await db.Meters.FindAsync(meterId))!.ShopId = other.Id; await db.SaveChangesAsync();
        }
        Assert.True((await Run(D(2026, 10, 1), D(2026, 10, 31))).Created);
        var invoices = await Invoices();
        Assert.Equal(0, invoices[1].PreviousBalance);
        Assert.Equal(50, invoices[1].Total);
        Assert.NotEqual(shopId, invoices[1].ShopId);
    }

    [Fact] public async Task UnpaidBalanceIsCarriedForwardAtTheSameShop()
    {
        await Readings(Good(At(2026, 8, 31, 23), 900), Good(At(2026, 9, 30, 23), 1000), Good(At(2026, 10, 31, 23), 1100));
        Assert.True((await Run(D(2026, 9, 1), D(2026, 9, 30))).Created);
        Assert.True((await Run(D(2026, 10, 1), D(2026, 10, 31))).Created);
        var invoices = await Invoices();
        Assert.Equal(50, invoices[1].PreviousBalance);
        Assert.Equal(100, invoices[1].Total);
    }

    [Fact] public async Task InvoiceNumbersAreNeverReused()
    {
        await Readings(Good(At(2026, 8, 31, 23), 900), Good(At(2026, 9, 30, 23), 1000), Good(At(2026, 10, 31, 23), 1100));
        Assert.True((await Run(D(2026, 9, 1), D(2026, 9, 30))).Created);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = Db(scope);
            db.Invoices.RemoveRange(db.Invoices); await db.SaveChangesAsync();
        }
        Assert.True((await Run(D(2026, 10, 1), D(2026, 10, 31))).Created);
        Assert.Equal("INV-000002", Assert.Single(await Invoices()).InvoiceNumber);
    }

    [Fact] public async Task OneMeterNotReadyMeansNothingIsPublished()
    {
        await Readings(Good(At(2026, 9, 30, 23), 1000), Good(At(2026, 10, 31, 23), 1100));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = Db(scope);
            db.Meters.Add(new Meter { Name = "M2", SerialNumber = "S2", ShopId = shopId, ControllerId = (await db.Meters.FindAsync(meterId))!.ControllerId, Active = true });
            await db.SaveChangesAsync();
        }
        var outcome = await Run(D(2026, 10, 1), D(2026, 10, 31));
        Assert.False(outcome.Created);
        Assert.Contains("M2: no reading", outcome.Message);
        await using var check = provider.CreateAsyncScope();
        Assert.Empty(await Db(check).BillingPeriods.ToListAsync());
    }

    // ---- automatic monthly schedule, driven by a fake clock ----

    InvoiceSchedulerService Scheduler() => new(provider.GetRequiredService<IServiceScopeFactory>(), clock, NullLogger<InvoiceSchedulerService>.Instance);

    async Task<InvoiceSchedule> AddSchedule(int startDay, int delay, DateOnly firstRun)
    {
        await using var scope = provider.CreateAsyncScope();
        var schedule = new InvoiceSchedule { CycleStartDay = startDay, PublishDelayDays = delay, DueDays = 10, GenerateAt = BillingCycle.LocalTime(firstRun, BillingCycle.RunTime), CreatedBy = "test" };
        Db(scope).InvoiceSchedules.Add(schedule); await Db(scope).SaveChangesAsync();
        return schedule;
    }

    async Task<InvoiceSchedule> Schedule()
    {
        await using var scope = provider.CreateAsyncScope();
        return await Db(scope).InvoiceSchedules.AsNoTracking().SingleAsync(x => !x.Completed);
    }

    [Fact] public async Task ScheduleWaitsForAMeterInsteadOfSkippingTheMonth()
    {
        clock.SetUtcNow(At(2026, 12, 1, 1));
        await Readings(Good(At(2026, 10, 31, 23), 1000), Review(At(2026, 11, 30, 23), 10));
        await AddSchedule(1, 0, D(2026, 12, 1));
        await Scheduler().RunDueSchedulesAsync();
        var waiting = await Schedule();
        Assert.Equal(BillingCycle.LocalTime(D(2026, 12, 1), BillingCycle.RunTime), waiting.GenerateAt); // not moved to January
        Assert.Contains("Waiting", waiting.Result);
        Assert.Empty(await Invoices());

        // The administrator ignores the glitch; the hourly retry then publishes November.
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = Db(scope);
            var review = await db.MeterReadings.SingleAsync(x => x.RequiresReview);
            review.RequiresReview = false; review.Quality = BillingRunService.RejectedQuality;
            db.MeterReadings.Add(new MeterReading { MeterId = meterId, AccumulatedKwh = 1100, Timestamp = At(2026, 11, 30, 23, 30), Quality = "Good" });
            await db.SaveChangesAsync();
        }
        clock.Advance(TimeSpan.FromMinutes(30));
        await Scheduler().RunDueSchedulesAsync();
        Assert.Empty(await Invoices()); // still inside the retry interval
        clock.Advance(TimeSpan.FromMinutes(31));
        await Scheduler().RunDueSchedulesAsync();
        var invoice = Assert.Single(await Invoices());
        Assert.Equal(100, invoice.ConsumptionKwh);
        Assert.Equal(BillingCycle.LocalTime(D(2027, 1, 1), BillingCycle.RunTime), (await Schedule()).GenerateAt);
    }

    [Fact] public async Task ScheduleCatchesUpEveryMissedMonth()
    {
        await Readings(Good(At(2026, 8, 31, 23), 900), Good(At(2026, 9, 30, 23), 1000), Good(At(2026, 10, 31, 23), 1100), Good(At(2026, 11, 30, 23), 1300));
        await AddSchedule(1, 0, D(2026, 10, 1));
        clock.SetUtcNow(At(2026, 12, 2, 9));
        await Scheduler().RunDueSchedulesAsync();
        var invoices = await Invoices();
        Assert.Equal(new decimal[] { 100, 100, 200 }, invoices.Select(x => x.ConsumptionKwh));
        Assert.Equal(BillingCycle.LocalTime(D(2027, 1, 1), BillingCycle.RunTime), (await Schedule()).GenerateAt);
    }

    [Fact] public async Task MidMonthCycleIsPublishedAfterTheDelay()
    {
        await Readings(Good(At(2026, 10, 24, 23), 1000), Good(At(2026, 11, 24, 23), 1080));
        await AddSchedule(25, 1, D(2026, 11, 26));
        clock.SetUtcNow(At(2026, 11, 25, 12));
        await Scheduler().RunDueSchedulesAsync();
        Assert.Empty(await Invoices()); // not yet the publication day
        clock.SetUtcNow(At(2026, 11, 26, 0, 10));
        await Scheduler().RunDueSchedulesAsync();
        var invoice = Assert.Single(await Invoices());
        Assert.Equal(80, invoice.ConsumptionKwh);
        await using var scope = provider.CreateAsyncScope();
        var period = await Db(scope).BillingPeriods.SingleAsync();
        Assert.Equal((D(2026, 10, 25), D(2026, 11, 24)), (period.StartDate, period.EndDate));
        Assert.Equal(BillingCycle.LocalTime(D(2026, 12, 26), BillingCycle.RunTime), (await Schedule()).GenerateAt);
    }
}

public sealed class OperationsHelperTests
{
    [Theory]
    [InlineData("Meter-01", "Meter-01")]
    [InlineData("Shop, Ground", "\"Shop, Ground\"")]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("-5", "'-5")]
    public void CsvFieldsAreEscaped(string value, string expected) => Assert.Equal(expected, IndexModel.Csv(value));
}

public sealed class BillingCycleMigrationTests
{
    [Fact] public async Task ExistingScheduleKeepsItsPublicationDates()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20260909061947_BacnetMeterPoints");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO InvoiceSchedules (PublicationDay,GenerateAt,DueDays,Completed,CreatedAt,CreatedBy) VALUES (5,'2026-12-05 00:05:00+03:00',10,0,'2026-11-01 00:00:00+00:00','admin');");
        await db.Database.MigrateAsync();
        var schedule = await db.InvoiceSchedules.SingleAsync();
        Assert.Equal(1, schedule.CycleStartDay);
        Assert.Equal(4, schedule.PublishDelayDays);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
