using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MallEnergyBilling.Tests;

public sealed class BacnetTests
{
    private static BacnetIpService Service(FakeTransport transport) => new(transport, NullLogger<BacnetIpService>.Instance);
    private static BacnetPoint Point(int id=1) => new() { Id=id, Name="Voltage", ObjectType="AnalogInput", ObjectInstance=id, Scale=2, Offset=3 };
    public static IEnumerable<object[]> Numbers() => new object[][] { [1.5f, 1.5m], [2.25d, 2.25m], [uint.MaxValue, (decimal)uint.MaxValue], [-123L,-123m], [true,1m], [false,0m], [ulong.MaxValue,(decimal)ulong.MaxValue], [DayOfWeek.Tuesday,2m] };
    [Theory, MemberData(nameof(Numbers))] public void ConvertsNumericTypes(object raw, decimal expected) => Assert.Equal(expected,BacnetIpService.Numeric(raw));
    [Theory, InlineData(null), InlineData("123"), InlineData(double.NaN), InlineData(double.PositiveInfinity)]
    public void RejectsInvalidValues(object? raw) => Assert.Throws<InvalidDataException>(()=>BacnetIpService.Numeric(raw));
    [Theory, InlineData(-1), InlineData(4194303)] public void RejectsInvalidObjectInstances(int instance) { var p=Point();p.ObjectInstance=instance;Assert.Throws<InvalidOperationException>(()=>BacnetIpService.ValidatePoint(p)); }
    [Fact] public void EverySupportedObjectParses() { foreach(var type in BacnetIpService.ObjectTypes) { var p=Point();p.ObjectType=type;BacnetIpService.ValidatePoint(p);BacnetTransport.ObjectType(type); } Assert.Throws<InvalidOperationException>(()=>BacnetTransport.ObjectType("ModbusRegister")); }
    [Theory, InlineData(0), InlineData(4194302), InlineData(null)]
    public void ValidatesDeviceInstanceRangeIncludingUnknown(int? instance)
    { BacnetNetwork.Validate(new(){BacnetLocalIp="127.0.0.1",IpAddress="127.0.0.2",BacnetDeviceInstance=instance}); }
    [Theory, InlineData(-1), InlineData(4194303)]
    public void RejectsReservedOrNegativeDeviceInstances(int instance)
    { Assert.Throws<InvalidOperationException>(()=>BacnetNetwork.Validate(new(){BacnetLocalIp="127.0.0.1",IpAddress="127.0.0.2",BacnetDeviceInstance=instance})); }
    [Fact] public void RejectsInvalidNetworkAndResilienceSettings()
    {
        foreach(var c in new Controller[]{new(){BacnetLocalIp="::1"},new(){BacnetLocalIp="192.0.2.254"},new(){BacnetLocalIp="127.0.0.1",IpAddress="bad/host"},new(){BacnetLocalIp="127.0.0.1",BacnetUdpPort=0},new(){BacnetLocalIp="127.0.0.1",TimeoutMilliseconds=0},new(){BacnetLocalIp="127.0.0.1",RetryCount=-1}})
            Assert.Throws<InvalidOperationException>(()=>BacnetNetwork.Validate(c));
    }
    [Fact] public void EnergyRequiresExplicitConfirmationAndKwh() { var p=Point();p.Measurement=MeasurementType.TotalImportEnergy;Assert.Throws<InvalidOperationException>(()=>BacnetIpService.ValidatePoint(p));p.BillingConfirmed=true;p.Unit="kWh";BacnetIpService.ValidatePoint(p);p.Unit="Wh";Assert.Throws<InvalidOperationException>(()=>BacnetIpService.ValidatePoint(p)); }
    [Fact] public async Task UnknownDeviceAndActualZeroAreDistinct()
    {
        var service=Service(new());var c=new Controller();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.TestAsync(c,default));
        c.BacnetDeviceInstance=0;Assert.Equal(0,(await service.TestAsync(c,default)).Instance);
    }
    [Fact] public async Task DeviceMismatchFails()
    { var service=Service(new FakeTransport { WrongIdentity=true }); await Assert.ThrowsAsync<InvalidOperationException>(()=>service.TestAsync(new(){BacnetDeviceInstance=0},default)); }
    [Fact] public async Task RpmFallbackKeepsOtherPointsAliveAndScales()
    {
        var fake=new FakeTransport { FailRpm=true, FailedInstance=1 };
        var rows=await Service(fake).ReadPointsAsync(new(),[Point(1),Point(2)],default);
        Assert.Equal("Timeout",rows.Single(x=>x.PointId==1).Quality);
        Assert.Equal(23m,rows.Single(x=>x.PointId==2).Value);Assert.True(fake.IndividualReads>0);
    }
    [Fact] public async Task UsesRpmAndDetectsStatusFault()
    {
        var fake=new FakeTransport();var row=(await Service(fake).ReadPointsAsync(new(),[Point()],default)).Single();
        Assert.Equal("Good",row.Quality);Assert.Equal(0,fake.IndividualReads);
        fake.Fault=true;row=(await Service(fake).ReadPointsAsync(new(),[Point()],default)).Single();Assert.Equal("Bad",row.Quality);
    }
    [Fact] public async Task InvalidDatatypeDoesNotCrashOtherPoints()
    {
        var fake=new FakeTransport { InvalidInstance=1 };var rows=await Service(fake).ReadPointsAsync(new(),[Point(1),Point(2)],default);
        Assert.Equal("Bad",rows.Single(x=>x.PointId==1).Quality);Assert.Equal("Good",rows.Single(x=>x.PointId==2).Quality);
    }
    [Fact] public async Task ObjectListFallsBackToIndexedAccess()
    {
        var fake=new FakeTransport { IndexedList=true };var objects=await Service(fake).BrowseAsync(new(){BacnetDeviceInstance=0},default);
        Assert.Single(objects);Assert.Equal("AnalogInput",objects[0].Type);Assert.Equal(7,objects[0].Instance);Assert.Equal("kWh",objects[0].Unit);
    }
    [Fact] public void TimeoutPreservesLastTimestampAndReconnectRestoresQuality()
    {
        var p=Point();var at=DateTimeOffset.UtcNow;
        BacnetPointState.Apply(p,new(1,10,23,"Good","",at));
        BacnetPointState.Apply(p,new(1,null,null,"Timeout","No reply",at.AddSeconds(5)));
        Assert.Equal(23m,p.LastValue);Assert.Equal(at,p.LastSuccess);Assert.Equal(1,p.ConsecutiveFailures);
        BacnetPointState.Apply(p,new(1,11,25,"Good","",at.AddSeconds(10)));
        Assert.Equal(0,p.ConsecutiveFailures);Assert.Equal("Good",p.EffectiveQuality(5,at.AddSeconds(11)));
        Assert.Equal("Stale",p.EffectiveQuality(5,at.AddMinutes(1)));
    }
    [Fact] public void BillingRejectsStaleAndCounterReviewButLeavesModbusUnchanged()
    {
        var end=DateTimeOffset.UtcNow;var row=new MeterReading{Timestamp=end.AddHours(-1)};
        var m=new Meter{Controller=new(){CommunicationType="BacnetIp"}};
        Assert.NotNull(BacnetBillingGuard.Validate(m,[row],row,end));
        row.Timestamp=end.AddMinutes(-1);Assert.Null(BacnetBillingGuard.Validate(m,[row],row,end));
        m.BacnetCounterReview=true;Assert.NotNull(BacnetBillingGuard.Validate(m,[row],row,end));
        m.Controller.CommunicationType="ModbusRtu";Assert.Null(BacnetBillingGuard.Validate(m,[row],row,end));
    }
    [Theory, InlineData("3","A"),InlineData("5","V"),InlineData("48","kW"),InlineData("19","kWh"),InlineData("12","kvar"),InlineData("204","kvarh")]
    public void UnitsUseBacnetIds(string id,string expected)=>Assert.Equal(expected,BacnetIpService.UnitName(id));

    internal sealed class FakeTransport : IBacnetTransport
    {
        public bool FailRpm, WrongIdentity, Fault, IndexedList;
        public int FailedInstance=-1,InvalidInstance=-1,IndividualReads;
        public Task<IReadOnlyList<BacnetDevice>> DiscoverAsync(Controller c,CancellationToken ct)=>Task.FromResult<IReadOnlyList<BacnetDevice>>([new(0,"192.0.2.1",47808,42)]);
        private IReadOnlyList<object?> Value(BacnetRequest r)
        {
            if(r.Property==75)return [new BacnetObject("Device",WrongIdentity?12:r.Object.Instance)];
            if(r.Property==76){if(IndexedList&&r.ArrayIndex==uint.MaxValue)throw new IOException("Abort");return r.ArrayIndex==0?[1u]:[new BacnetObject("AnalogInput",7)];}
            if(r.Property==85){if(r.Object.Instance==FailedInstance)throw new TimeoutException();return r.Object.Instance==InvalidInstance?[null]:[10f];}
            return r.Property switch {103=>[0u],111=>[Fault?"0100":"0000"],117=>[19u],77=>["Test meter"],28=>["Energy"],120=>[42u],_=>[""]};
        }
        public Task<IReadOnlyList<object?>> ReadAsync(Controller c,BacnetRequest r,CancellationToken ct){ct.ThrowIfCancellationRequested();IndividualReads++;return Task.FromResult(Value(r));}
        public Task<IReadOnlyDictionary<BacnetRequest,IReadOnlyList<object?>>> ReadMultipleAsync(Controller c,IReadOnlyList<BacnetRequest> requests,CancellationToken ct)
        {if(FailRpm)throw new IOException("Reject RPM");return Task.FromResult<IReadOnlyDictionary<BacnetRequest,IReadOnlyList<object?>>>(requests.ToDictionary(r=>r,Value));}
    }
}
