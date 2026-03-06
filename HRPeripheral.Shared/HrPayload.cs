namespace HRPeripheral;

/// <summary>
/// Pure C# helper for building and parsing BLE Heart Rate Measurement payloads.
/// No Android dependencies — safe to unit test.
/// </summary>
public static class HrPayload
{
    /// <summary>
    /// Builds a BLE Heart Rate Measurement payload per the Bluetooth SIG spec.
    /// Flags byte 0x00 = UInt8 HR format, no extra fields.
    /// HR value clamped to [0, 255].
    /// </summary>
    public static byte[] Build(int bpm)
    {
        byte flags = 0x00;
        byte hr = (byte)Math.Max(0, Math.Min(255, bpm));
        return new byte[] { flags, hr };
    }

    /// <summary>
    /// Extracts the BPM value from a standard HR measurement payload.
    /// Supports both UInt8 (flags bit0 = 0) and UInt16 (flags bit0 = 1) formats.
    /// Returns -1 if the payload is malformed.
    /// </summary>
    public static int Parse(byte[]? payload)
    {
        if (payload == null || payload.Length < 2) return -1;
        bool is16Bit = (payload[0] & 0x01) != 0;
        if (is16Bit)
        {
            if (payload.Length < 3) return -1;
            return payload[1] | (payload[2] << 8);
        }
        return payload[1];
    }
}
