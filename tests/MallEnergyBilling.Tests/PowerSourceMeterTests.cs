using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Pages.PowerSources;

namespace MallEnergyBilling.Tests;

public sealed class PowerSourceMeterTests
{
    private readonly Meter meter = new()
    {
        Id = 42,
        Name = "Main incomer",
        SerialNumber = "UM-9001",
        PowerSource = PowerSource.Grid,
        Shop = new Shop
        {
            ShopNumber = "SHOP-12",
            Name = "Coffee Corner",
            TenantName = "Kampala Foods",
            Floor = "Level 2",
            Zone = "East Wing"
        }
    };

    [Theory]
    [InlineData("42")]
    [InlineData("UM-9001")]
    [InlineData("main incomer")]
    [InlineData("Kampala Foods")]
    [InlineData("SHOP-12")]
    [InlineData("East Wing")]
    public void SearchesEverySupportedMeterField(string search) =>
        Assert.True(PowerSourceMetersPageModel.MeterMatchesSearch(meter, search));

    [Fact]
    public void RejectsUnrelatedSearch() =>
        Assert.False(PowerSourceMetersPageModel.MeterMatchesSearch(meter, "Generator room 99"));

    [Fact]
    public void SupportsOnlyRequestedPowerSources() =>
        Assert.Equal([PowerSource.Grid, PowerSource.Generator], Enum.GetValues<PowerSource>());
}
