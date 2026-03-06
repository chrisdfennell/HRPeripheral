using Xunit;
using HRPeripheral;

namespace HRPeripheral.Tests;

public class HrPayloadTests
{
    [Theory]
    [InlineData(72)]
    [InlineData(0)]
    [InlineData(255)]
    public void Build_RoundTrip(int bpm)
    {
        var payload = HrPayload.Build(bpm);
        Assert.Equal(2, payload.Length);
        Assert.Equal(0x00, payload[0]); // flags = UInt8 format
        Assert.Equal((byte)bpm, payload[1]);
        Assert.Equal(bpm, HrPayload.Parse(payload));
    }

    [Fact]
    public void Build_ClampsTooHigh()
    {
        var payload = HrPayload.Build(300);
        Assert.Equal(255, payload[1]);
    }

    [Fact]
    public void Build_ClampsNegative()
    {
        var payload = HrPayload.Build(-5);
        Assert.Equal(0, payload[1]);
    }

    [Fact]
    public void Parse_NullReturnsNegOne()
    {
        Assert.Equal(-1, HrPayload.Parse(null));
    }

    [Fact]
    public void Parse_EmptyReturnsNegOne()
    {
        Assert.Equal(-1, HrPayload.Parse(Array.Empty<byte>()));
    }

    [Fact]
    public void Parse_SingleByteReturnsNegOne()
    {
        Assert.Equal(-1, HrPayload.Parse(new byte[] { 0x00 }));
    }

    [Fact]
    public void Parse_UInt16Format()
    {
        // flags bit0=1 means UInt16 HR value
        var payload = new byte[] { 0x01, 0x20, 0x01 }; // 0x0120 = 288
        Assert.Equal(288, HrPayload.Parse(payload));
    }

    [Fact]
    public void Parse_UInt16TooShortReturnsNegOne()
    {
        // flags says UInt16 but only 2 bytes total
        var payload = new byte[] { 0x01, 0x20 };
        Assert.Equal(-1, HrPayload.Parse(payload));
    }
}
