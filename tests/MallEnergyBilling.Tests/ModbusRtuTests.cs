using MallEnergyBilling.Web.Models;using MallEnergyBilling.Web.Services;namespace MallEnergyBilling.Tests;
public sealed class ModbusRtuTests
{
 [Fact]public void CalculatesStandardModbusCrc(){byte[] frame=[0x01,0x03,0x00,0x00,0x00,0x0A];Assert.Equal(0xCDC5,ModbusRtuService.Crc16(frame));}
 [Fact]public void ConvertsDeltaDmovLowHighWords(){ushort[] registers=[0xE240,0x0001];Assert.Equal(1234.56m,ModbusValueConverter.ConvertValue(registers,RegisterDataType.UInt32,WordOrder.LowHigh,.01m));}
 [Theory][InlineData(RegisterDataType.UInt16,1)][InlineData(RegisterDataType.UInt32,2)][InlineData(RegisterDataType.Float32,2)][InlineData(RegisterDataType.UInt64,4)]public void UsesCorrectRegisterCount(RegisterDataType type,ushort expected)=>Assert.Equal(expected,ModbusValueConverter.RegisterCount(type));
}
