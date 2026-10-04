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
    /// <summary>A stalled fan (below <see cref="TelemetrySettings.StalledFanRpm"/>) while the CPU
    /// temperature is not OK is critical.</summary>
    CpuFanStall,
    /// <summary>As <see cref="CpuFanStall"/>, against the GPU temperature.</summary>
    GpuFanStall
}

/// <summary>
/// The alert rules of the hardware-display design (report §04). CPU limits are relative to the
/// user's TjMax because there is no single safe CPU temperature; the GPU, storage and RAM limits
/// come from <see cref="TelemetrySettings"/>. Clock, power and transfer rates never alert.
/// </summary>
internal static class Thresholds
{
    /// <summary>A state drops back only once the value is this far below the limit, so it does
    /// not flicker at a boundary.</summary>
    public const double Hysteresis = 3.0;

    /// <summary>The warn and critical limits of a bounded rule, or null for none.</summary>
    public static (double Warn, double Critical)? LimitsFor(ThresholdKind kind, TelemetrySettings settings) => kind switch
    {
        ThresholdKind.CpuTemperature => (settings.TjMax - 15, settings.TjMax - 5),
        ThresholdKind.GpuTemperature => (settings.GpuWarn, settings.GpuCritical),
        ThresholdKind.StorageTemperature => (settings.StorageWarn, settings.StorageCritical),
        ThresholdKind.RamLoad => (settings.RamWarn, settings.RamCritical),
        _ => null
    };

    /// <summary>
    /// The new state of a metric. <paramref name="companion"/> is the state of the temperature a
    /// fan rule watches; ignored by every other rule.
    /// </summary>
    public static MetricState Evaluate(ThresholdKind kind, double value, TelemetrySettings settings,
        MetricState previous, MetricState companion)
    {
        if (double.IsNaN(value))
            return MetricState.Ok;

        if (kind is ThresholdKind.CpuFanStall or ThresholdKind.GpuFanStall)
            return value < settings.StalledFanRpm && companion != MetricState.Ok ? MetricState.Critical : MetricState.Ok;

        if (LimitsFor(kind, settings) is not { } limits)
            return MetricState.Ok;

        if (value >= limits.Critical || (previous == MetricState.Critical && value > limits.Critical - Hysteresis))
            return MetricState.Critical;

        if (value >= limits.Warn || (previous >= MetricState.Warn && value > limits.Warn - Hysteresis))
            return MetricState.Warn;

        return MetricState.Ok;
    }
}
