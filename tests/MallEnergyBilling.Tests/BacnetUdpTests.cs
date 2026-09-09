using System.IO.BACnet;
using System.Net;
using System.Net.Sockets;
using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MallEnergyBilling.Tests;

public sealed class BacnetUdpTests
{
    [Fact]
    public async Task ProductionTransportReadsRealUdpDeviceObjectAndRpmAndTimesOut()
    {
        using var reserve=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
        var port=((IPEndPoint)reserve.Client.LocalEndPoint!).Port;reserve.Close();
        // Test-only BACnet peer. Production DI never registers a simulator.
        using var peer=new BacnetClient(new BacnetIpUdpProtocolTransport(port,useExclusivePort:true,localEndpointIp:"127.0.0.2"));
        var seen=0;
        IList<BacnetValue> Value(BacnetObjectId obj,BacnetPropertyReference property) => property.propertyIdentifier switch
        {
            75 => [new BacnetValue(obj)], 77 => [new BacnetValue("UDP energy meter")],
            85 => [new BacnetValue(18532.6d)], 103 => [new BacnetValue(0u)],
            111 => [new BacnetValue(BacnetBitString.Parse("0000"))], 117 => [new BacnetValue(19u)],
            _ => [new BacnetValue(0u)]
        };
        peer.OnReadPropertyRequest+=(sender,address,invoke,obj,property,max)=>
        {Interlocked.Increment(ref seen);sender.ReadPropertyResponse(address,invoke,sender.GetSegmentBuffer(max),obj,property,Value(obj,property));};
        peer.OnReadPropertyMultipleRequest+=(sender,address,invoke,specs,max)=>
        {Interlocked.Increment(ref seen);sender.ReadPropertyMultipleResponse(address,invoke,sender.GetSegmentBuffer(max),specs.Select(s=>new BacnetReadAccessResult(s.objectIdentifier,s.propertyReferences.Select(p=>new BacnetPropertyValue{property=p,value=Value(s.objectIdentifier,p)}).ToArray())).ToArray());};
        peer.Start();
        using var transport=new BacnetTransport(NullLoggerFactory.Instance);
        var config=new Controller{BacnetLocalIp="127.0.0.1",IpAddress="127.0.0.2",BacnetUdpPort=port,BacnetDeviceInstance=0,TimeoutMilliseconds=1000,RetryCount=0};
        var service=new BacnetIpService(transport,NullLogger<BacnetIpService>.Instance);
        var device=await service.TestAsync(config,default);Assert.Equal(0,device.Instance);Assert.Equal("UDP energy meter",device.Name);
        var value=(await service.ReadPointsAsync(config,[new(){Id=1,Name="Energy",ObjectType="Accumulator",ObjectInstance=12}],default)).Single();
        Assert.Equal("Good",value.Quality);Assert.Equal(18532.6m,value.Value);Assert.True(seen>=5);
        config.IpAddress="127.0.0.3";config.TimeoutMilliseconds=100;
        await Assert.ThrowsAsync<TimeoutException>(()=>transport.ReadAsync(config,new(new("AnalogInput",1),85),default));
    }
}
