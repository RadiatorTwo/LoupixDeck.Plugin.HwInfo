using System.Collections.Concurrent;

namespace LoupixDeck.Plugin.HwInfo.Telemetry;

/// <summary>
/// One tracked metric as of the latest sample: the (smoothed) value in native units, its alert
/// state, the history oldest → newest (NaN where a sample was missing) and everything needed to
/// draw it.
/// </summary>
internal sealed record MetricSnapshot(
    double Value,
    MetricState State,
    ReadOnlyMemory<double> History,
    double Min,
    double Max,
    double? WarnAt,
    MetricFormat Format,
    string? Unit)
{
    public bool HasRange => Max > Min;

    /// <summary>Bar fill 0..1 for the current value.</summary>
    public double Fraction => HasRange ? Math.Clamp((Value - Min) / (Max - Min), 0.0, 1.0) : 0.0;

    public (string Value, string Unit) Formatted => MetricFormatter.Format(Value, Format, Unit);
}

/// <summary>
/// An immutable sample of everything the tiles draw, published once a second by
/// <see cref="TelemetrySampler"/>. Metrics are keyed by <see cref="MetricKeys.ForSensor"/> for single
/// sensors and by the <see cref="PageMetrics"/> ids for derived ones.
/// </summary>
internal sealed class TelemetryFrame(
    bool isAvailable,
    IReadOnlyList<HwInfoSensor> sensors,
    IReadOnlyDictionary<string, MetricSnapshot> metrics,
    ConcurrentDictionary<string, byte>? requested = null)
{
    public static TelemetryFrame Unavailable { get; } = new(false, [], new Dictionary<string, MetricSnapshot>());

    public bool IsAvailable { get; } = isAvailable;

    public IReadOnlyList<HwInfoSensor> Sensors { get; } = sensors;

    /// <summary>The metric under <paramref name="key"/>. A sensor metric is sampled only once a tile
    /// asks for it: a miss registers the key, and the next sample (within a second) tracks it.</summary>
    public MetricSnapshot? Get(string key)
    {
        if (metrics.TryGetValue(key, out MetricSnapshot? metric))
            return metric;

        requested?.TryAdd(key, 0);
        return null;
    }
}
