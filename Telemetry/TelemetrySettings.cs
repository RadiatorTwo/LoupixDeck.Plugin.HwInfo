namespace LoupixDeck.Plugin.HwInfo.Telemetry;

/// <summary>
/// The user's choices the sampler applies: alert limits (temperatures in °C, RAM load in %, fan
/// speed in RPM). Read once per sample, so a changed setting shows on the next one.
/// </summary>
internal sealed record TelemetrySettings(
    double TjMax,
    double GpuWarn,
    double GpuCritical,
    double StorageWarn,
    double StorageCritical,
    double RamWarn,
    double RamCritical,
    double StalledFanRpm)
{
    /// <summary>The design's starting points (report §04), with TjMax 100 °C.</summary>
    public static TelemetrySettings Default { get; } = new(100, 80, 88, 55, 65, 85, 95, 200);
}
