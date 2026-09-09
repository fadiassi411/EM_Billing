using System.ComponentModel.DataAnnotations;

namespace MallEnergyBilling.Web.Models;

public enum MeasurementType
{
    Unmapped, VoltageL1, VoltageL2, VoltageL3, VoltageL1L2, VoltageL2L3, VoltageL3L1,
    CurrentL1, CurrentL2, CurrentL3, ActivePowerL1, ActivePowerL2, ActivePowerL3,
    TotalActivePower, ReactivePower, ApparentPower, PowerFactorL1, PowerFactorL2,
    PowerFactorL3, TotalPowerFactor, Frequency, TotalImportEnergy, TotalExportEnergy,
    ReactiveEnergy, Demand, MeterStatus
}

public sealed class BacnetPoint
{
    public int Id { get; set; }
    public int MeterId { get; set; }
    public Meter? Meter { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    public MeasurementType Measurement { get; set; }
    [Required] public string ObjectType { get; set; } = "AnalogValue";
    [Range(0, 4194302)] public int ObjectInstance { get; set; }
    [Range(0, 4194303)] public int PropertyId { get; set; } = 85;
    [StringLength(50), DisplayFormat(ConvertEmptyStringToNull = false)] public string Unit { get; set; } = "";
    [Range(typeof(decimal), "-1000000", "1000000")] public decimal Scale { get; set; } = 1;
    [Range(typeof(decimal), "-1000000000000", "1000000000000")] public decimal Offset { get; set; }
    public bool Enabled { get; set; } = true;
    public bool BillingConfirmed { get; set; }
    public decimal? LastValue { get; set; }
    public decimal? LastRawValue { get; set; }
    public DateTimeOffset? LastAttempt { get; set; }
    public DateTimeOffset? LastSuccess { get; set; }
    public string Quality { get; set; } = "Not read";
    public string LastError { get; set; } = "";
    public int ConsecutiveFailures { get; set; }
    public string EffectiveQuality(int intervalSeconds, DateTimeOffset now) => !Enabled ? "Disabled" :
        Quality == "Good" && (LastSuccess is null || now - LastSuccess > TimeSpan.FromSeconds(Math.Max(15, intervalSeconds * 3))) ? "Stale" : Quality;
}
