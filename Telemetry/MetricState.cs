namespace LoupixDeck.Plugin.HwInfo.Telemetry;

/// <summary>Alert level of a metric. Ordered, so the worst of several is their maximum.</summary>
internal enum MetricState
{
    Ok = 0,
    Warn = 1,
    Critical = 2
}
