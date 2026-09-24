namespace LoupixDeck.Plugin.HwInfo.Telemetry;

/// <summary>Which alert rule a metric follows.</summary>
internal enum ThresholdKind
{
    None,
    /// <summary>TjMax − 15 / TjMax − 5 °C.</summary>
    CpuTemperature,
    GpuTemperature,
    StorageTemperature,
    RamLoad,
    /// <summary>A stalled fan (&lt; 200 RPM) while the CPU temperature is not OK is critical.</summary>
    CpuFanStall,
    /// <summary>As <see cref="CpuFanStall"/>, against the GPU temperature.</summary>
    GpuFanStall
}

/// <summary>
/// The alert rules of the hardware-display design (report §04). CPU limits are relative to the
/// user's TjMax because there is no single safe CPU temperature; the GPU, storage and RAM limits
/// are the design's starting points. Clock, power and transfer rates never alert.
/// </summary>
internal static class Thresholds
{
    /// <summary>A state drops back only once the value is this far below the limit, so it does
    /// not flicker at a boundary.</summary>
    public const double Hysteresis = 3.0;

    public const double StalledFanRpm = 200.0;

    /// <summary>The warn and critical limits of a bounded rule, or null for none.</summary>
    public static (double Warn, double Critical)? LimitsFor(ThresholdKind kind, double tjMax) => kind switch
    {
        ThresholdKind.CpuTemperature => (tjMax - 15, tjMax - 5),
        ThresholdKind.GpuTemperature => (80, 88),
        ThresholdKind.StorageTemperature => (55, 65),
        ThresholdKind.RamLoad => (85, 95),
        _ => null
    };

    /// <summary>
    /// The new state of a metric. <paramref name="companion"/> is the state of the temperature a
    /// fan rule watches; ignored by every other rule.
    /// </summary>
    public static MetricState Evaluate(ThresholdKind kind, double value, double tjMax, MetricState previous,
        MetricState companion)
    {
        if (double.IsNaN(value))
            return MetricState.Ok;

        if (kind is ThresholdKind.CpuFanStall or ThresholdKind.GpuFanStall)
            return value < StalledFanRpm && companion != MetricState.Ok ? MetricState.Critical : MetricState.Ok;

        if (LimitsFor(kind, tjMax) is not { } limits)
            return MetricState.Ok;

        if (value >= limits.Critical || (previous == MetricState.Critical && value > limits.Critical - Hysteresis))
            return MetricState.Critical;

        if (value >= limits.Warn || (previous >= MetricState.Warn && value > limits.Warn - Hysteresis))
            return MetricState.Warn;

        return MetricState.Ok;
    }
}
