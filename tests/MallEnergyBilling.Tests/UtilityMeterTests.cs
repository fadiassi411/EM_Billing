using MallEnergyBilling.Web.Models;

namespace MallEnergyBilling.Tests;

public sealed class UtilityMeterTests
{
    [Fact]
    public void ExistingMetersDefaultToElectricityAndKwh()
    {
        var meter = new Meter();
        Assert.Equal(UtilityType.Electricity, meter.UtilityType);
        Assert.Equal("kWh", meter.Unit);
    }

    [Fact]
    public void WaterMetersUseCubicMetres()
    {
        var meter = new Meter { UtilityType = UtilityType.Water };
        Assert.Equal("m³", meter.Unit);
    }

    [Fact]
    public void SupportsInitialUtilityTypes() =>
        Assert.Equal([UtilityType.Electricity, UtilityType.Water], Enum.GetValues<UtilityType>());
}
