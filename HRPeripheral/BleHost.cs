namespace HRPeripheral
{
    /// <summary>
    /// Tiny bridge so Settings can access the running BlePeripheral instance.
    /// Assign this right after you create the peripheral (e.g., in HeartRateService).
    /// </summary>
    public static class BleHost
    {
        public static BlePeripheral? Peripheral { get; set; }
    }
}
