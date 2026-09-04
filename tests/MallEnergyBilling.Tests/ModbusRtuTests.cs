using System.Net;
using System.Net.Sockets;
using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
namespace MallEnergyBilling.Tests;
public sealed class ModbusRtuTests
{
 [Fact]public void CalculatesStandardModbusCrc(){byte[] frame=[0x01,0x03,0x00,0x00,0x00,0x0A];Assert.Equal(0xCDC5,ModbusService.Crc16(frame));}
 [Fact]public void ConvertsDeltaDmovLowHighWords(){ushort[] registers=[0xE240,0x0001];Assert.Equal(1234.56m,ModbusValueConverter.ConvertValue(registers,RegisterDataType.UInt32,WordOrder.LowHigh,.01m));}
 [Theory][InlineData(RegisterDataType.UInt16,1)][InlineData(RegisterDataType.UInt32,2)][InlineData(RegisterDataType.Float32,2)][InlineData(RegisterDataType.UInt64,4)]public void UsesCorrectRegisterCount(RegisterDataType type,ushort expected)=>Assert.Equal(expected,ModbusValueConverter.RegisterCount(type));
 [Fact]public async Task ReadsHoldingRegistersOverModbusTcp()
 {
  var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;
  var server=Task.Run(async()=>{using var client=await listener.AcceptTcpClientAsync();await using var stream=client.GetStream();var request=new byte[12];await stream.ReadExactlyAsync(request);Assert.Equal(new byte[]{0,3,0,2},request[8..12]);byte[] response=[request[0],request[1],0,0,0,7,1,3,4,0x12,0x34,0xAB,0xCD];await stream.WriteAsync(response);});
  try
  {
   var service=new ModbusService(NullLogger<ModbusService>.Instance);var controller=new Controller{CommunicationType="ModbusTcp",IpAddress="127.0.0.1",TcpPort=port,SlaveAddress=1,TimeoutMilliseconds=2000,RetryCount=0};
   var result=await service.ReadHoldingRegistersAsync(controller,3,2);Assert.Equal(new ushort[]{0x1234,0xABCD},result.Registers);await server;
  }
  finally{listener.Stop();}
 }
}
