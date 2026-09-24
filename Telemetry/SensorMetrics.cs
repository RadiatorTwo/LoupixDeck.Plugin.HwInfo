namespace LoupixDeck.Plugin.HwInfo.Telemetry;

/// <summary>
/// Describes a single HWiNFO reading as a metric: which native value to track, how to format it
/// and which alert rule applies. Decided by the reading type, the unit and the component
/// (<see cref="Components"/>). Used for every reading, so any sensor a user puts on a tile gets a
/// history and a state.
/// </summary>
internal static class SensorMetrics
{
    /// <summary>
    /// The value to track, in the unit <see cref="Describe"/> formats: temperatures in °C, transfer
    /// rates in bytes per second, data sizes in MB; everything else is HWiNFO's own value.
    /// </summary>
    public static double NativeValue(HwInfoSensor sensor)
    {
        double value = sensor.Value;
        if (sensor.Type == HwInfoReadingType.Temperature && sensor.Unit.EndsWith('F'))
            return (value - 32) * 5 / 9;
        if (MetricFormatter.ToBytesPerSecond(value, sensor.Unit) is { } rate)
            return rate;
        if (IsSize(sensor.Unit))
            return Megabytes(value, sensor.Unit);
        return value;
    }

    public static MetricInfo Describe(HwInfoSensor sensor, string component, double tjMax)
    {
        return sensor.Type switch
        {
            HwInfoReadingType.Temperature => Temperature(sensor, component, tjMax),

            HwInfoReadingType.Usage when component is Components.Cpu or Components.Gpu =>
                new MetricInfo(MetricFormat.Percent, 0, 100, Smooth: true),

            HwInfoReadingType.Fan when component == Components.Gpu =>
                new MetricInfo(MetricFormat.Rpm, 0, 3300, ThresholdKind.GpuFanStall, GrowToPeak: true),
            HwInfoReadingType.Fan =>
                new MetricInfo(MetricFormat.Rpm, 0, 3000, ThresholdKind.CpuFanStall, GrowToPeak: true),

            HwInfoReadingType.Power when sensor.Unit == "W" => new MetricInfo(MetricFormat.Watt, 0, 100, GrowToPeak: true),

            HwInfoReadingType.Clock when sensor.Unit == "MHz" && component == Components.Gpu =>
                new MetricInfo(MetricFormat.ClockMhz, 0, 3200, Smooth: true, GrowToPeak: true),
            HwInfoReadingType.Clock when sensor.Unit == "MHz" =>
                new MetricInfo(MetricFormat.ClockMhz, 0, 6000, Smooth: true, GrowToPeak: true),

            _ when sensor.Unit == "%" && IsPhysicalMemoryLoad(sensor) =>
                new MetricInfo(MetricFormat.Percent, 0, 100, ThresholdKind.RamLoad),
            _ when sensor.Unit == "%" => new MetricInfo(MetricFormat.Percent, 0, 100),

            // The CPU's ratios are its per-core multipliers.
            _ when sensor.Unit == "x" =>
                new MetricInfo(MetricFormat.Multiplier, 0, 60, Smooth: true, GrowToPeak: true),

            _ when IsSize(sensor.Unit) => new MetricInfo(MetricFormat.Megabytes, 0, 0),
            _ when MetricFormatter.ToBytesPerSecond(1, sensor.Unit) is not null =>
                new MetricInfo(MetricFormat.BytesPerSecond, 0, 0),

            _ => new MetricInfo(MetricFormat.Generic, 0, 0, Unit: sensor.Unit)
        };
    }

    /// <summary>
    /// Readings HWiNFO files as temperatures that are really fixed limits of the device ("GPU
    /// Thermal Limit", "Distance to TjMAX"). They get no alert rule and are left out of page values.
    /// </summary>
    public static bool IsTemperatureLimit(HwInfoSensor sensor) =>
        sensor.Type == HwInfoReadingType.Temperature
        && (Contains(Label(sensor), "Limit") || Contains(Label(sensor), "Threshold")
            || Contains(Label(sensor), "Critical") || Contains(Label(sensor), "Warning")
            || Contains(Label(sensor), "Distance to TjMAX"));

    /// <summary>Readings of the virtual memory and the page file, which the RAM page leaves out.</summary>
    public static bool IsVirtualMemory(HwInfoSensor sensor) =>
        Contains(Label(sensor), "Virtual") || Contains(Label(sensor), "Page File");

    public static bool IsPhysicalMemoryLoad(HwInfoSensor sensor) =>
        Label(sensor).Equals("Physical Memory Load", StringComparison.OrdinalIgnoreCase);

    /// <summary>The label to pick readings by: HWiNFO's English label, else the displayed one.</summary>
    public static string Label(HwInfoSensor sensor) =>
        sensor.LabelOrig.Length > 0 ? sensor.LabelOrig : sensor.Label;

    private static MetricInfo Temperature(HwInfoSensor sensor, string component, double tjMax)
    {
        if (IsTemperatureLimit(sensor))
            return new MetricInfo(MetricFormat.Temperature, 0, 0);

        return component switch
        {
            Components.Cpu => new MetricInfo(MetricFormat.Temperature, 30, tjMax, ThresholdKind.CpuTemperature),
            // Only the GPU core has the design's 80/88 limits; hot spot and memory run hotter by design.
            Components.Gpu => new MetricInfo(MetricFormat.Temperature, 30, 95,
                IsGpuCore(sensor) ? ThresholdKind.GpuTemperature : ThresholdKind.None),
            Components.Storage => new MetricInfo(MetricFormat.Temperature, 20, 80, ThresholdKind.StorageTemperature),
            _ => new MetricInfo(MetricFormat.Temperature, 20, 100)
        };
    }

    /// <summary>The GPU core temperature: HWiNFO calls it "GPU Temperature".</summary>
    public static bool IsGpuCore(HwInfoSensor sensor) =>
        Label(sensor).Equals("GPU Temperature", StringComparison.OrdinalIgnoreCase);

    private static bool IsSize(string unit) => unit is "GB" or "MB" or "KB" or "TB";

    private static double Megabytes(double value, string unit) => unit switch
    {
        "TB" => value * 1024 * 1024,
        "GB" => value * 1024,
        "KB" => value / 1024,
        _ => value
    };

    private static bool Contains(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);
}
