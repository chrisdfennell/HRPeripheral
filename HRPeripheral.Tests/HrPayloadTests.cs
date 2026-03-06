using Xunit;
using HRPeripheral;

namespace HRPeripheral.Tests;

public class HrPayloadTests
{
    // =====================================================================
    // Build - round-trip
    // =====================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(72)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(254)]
    [InlineData(255)]
    public void Build_RoundTrip(int bpm)
    {
        var payload = HrPayload.Build(bpm);
        Assert.Equal(2, payload.Length);
        Assert.Equal(0x00, payload[0]); // flags = UInt8 format
        Assert.Equal((byte)bpm, payload[1]);
        Assert.Equal(bpm, HrPayload.Parse(payload));
    }

    // =====================================================================
    // Build - clamping
    // =====================================================================

    [Theory]
    [InlineData(256, 255)]
    [InlineData(300, 255)]
    [InlineData(1000, 255)]
    [InlineData(int.MaxValue, 255)]
    public void Build_ClampsTooHigh(int input, byte expected)
    {
        var payload = HrPayload.Build(input);
        Assert.Equal(expected, payload[1]);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(-5, 0)]
    [InlineData(-100, 0)]
    [InlineData(int.MinValue, 0)]
    public void Build_ClampsNegative(int input, byte expected)
    {
        var payload = HrPayload.Build(input);
        Assert.Equal(expected, payload[1]);
    }

    // =====================================================================
    // Build - always produces valid format
    // =====================================================================

    [Fact]
    public void Build_AlwaysReturns2Bytes()
    {
        for (int bpm = -10; bpm <= 300; bpm += 50)
        {
            var payload = HrPayload.Build(bpm);
            Assert.Equal(2, payload.Length);
        }
    }

    [Fact]
    public void Build_FlagsByteAlwaysZero()
    {
        for (int bpm = 0; bpm <= 255; bpm++)
        {
            Assert.Equal(0x00, HrPayload.Build(bpm)[0]);
        }
    }

    // =====================================================================
    // Parse - null / empty / short
    // =====================================================================

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

    // =====================================================================
    // Parse - UInt8 format (flags bit0 = 0)
    // =====================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(72)]
    [InlineData(200)]
    [InlineData(255)]
    public void Parse_UInt8Format_ExtractsCorrectBpm(byte bpm)
    {
        var payload = new byte[] { 0x00, bpm };
        Assert.Equal(bpm, HrPayload.Parse(payload));
    }

    [Fact]
    public void Parse_UInt8Format_IgnoresExtraBytes()
    {
        // Extra bytes after the HR value should not matter
        var payload = new byte[] { 0x00, 120, 0xFF, 0xFF };
        Assert.Equal(120, HrPayload.Parse(payload));
    }

    [Fact]
    public void Parse_UInt8Format_FlagsWithOtherBitsSet()
    {
        // Other flag bits set but bit0=0 means UInt8
        var payload = new byte[] { 0xFE, 100 }; // 11111110 - bit0 is 0
        Assert.Equal(100, HrPayload.Parse(payload));
    }

    // =====================================================================
    // Parse - UInt16 format (flags bit0 = 1)
    // =====================================================================

    [Fact]
    public void Parse_UInt16Format()
    {
        // flags bit0=1 means UInt16 HR value (little-endian)
        var payload = new byte[] { 0x01, 0x20, 0x01 }; // 0x0120 = 288
        Assert.Equal(288, HrPayload.Parse(payload));
    }

    [Fact]
    public void Parse_UInt16Format_ZeroValue()
    {
        var payload = new byte[] { 0x01, 0x00, 0x00 }; // 0x0000 = 0
        Assert.Equal(0, HrPayload.Parse(payload));
    }

    [Fact]
    public void Parse_UInt16Format_MaxValue()
    {
        var payload = new byte[] { 0x01, 0xFF, 0xFF }; // 0xFFFF = 65535
        Assert.Equal(65535, HrPayload.Parse(payload));
    }

    [Fact]
    public void Parse_UInt16Format_LittleEndian()
    {
        // 0x0100 in little-endian: low=0x00, high=0x01 => 256
        var payload = new byte[] { 0x01, 0x00, 0x01 };
        Assert.Equal(256, HrPayload.Parse(payload));
    }

    [Fact]
    public void Parse_UInt16Format_WithExtraBytes()
    {
        var payload = new byte[] { 0x01, 0x64, 0x00, 0xFF, 0xFF }; // 100 in uint16
        Assert.Equal(100, HrPayload.Parse(payload));
    }

    [Fact]
    public void Parse_UInt16TooShortReturnsNegOne()
    {
        // flags says UInt16 but only 2 bytes total
        var payload = new byte[] { 0x01, 0x20 };
        Assert.Equal(-1, HrPayload.Parse(payload));
    }

    [Fact]
    public void Parse_UInt16Format_FlagsWithOtherBitsSet()
    {
        // Other flag bits set and bit0=1 means UInt16
        var payload = new byte[] { 0xFF, 0x64, 0x00 }; // 11111111 - bit0 is 1, value = 100
        Assert.Equal(100, HrPayload.Parse(payload));
    }

    // =====================================================================
    // Build then Parse round-trip with boundary values
    // =====================================================================

    [Fact]
    public void BuildThenParse_AllValidValues()
    {
        for (int bpm = 0; bpm <= 255; bpm++)
        {
            var payload = HrPayload.Build(bpm);
            Assert.Equal(bpm, HrPayload.Parse(payload));
        }
    }

    // =====================================================================
    // Independent payloads (no shared state)
    // =====================================================================

    [Fact]
    public void Build_ReturnsNewArrayEachTime()
    {
        var a = HrPayload.Build(100);
        var b = HrPayload.Build(100);
        Assert.NotSame(a, b);
    }

    [Fact]
    public void Build_MutatingReturnedArray_DoesNotAffectNextCall()
    {
        var a = HrPayload.Build(100);
        a[1] = 0;
        var b = HrPayload.Build(100);
        Assert.Equal(100, b[1]);
    }
}
