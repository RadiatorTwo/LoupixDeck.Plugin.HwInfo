using LoupixDeck.Plugin.HwInfo.Rendering.Tiles;
using LoupixDeck.Plugin.HwInfo.Telemetry;

namespace LoupixDeck.Plugin.HwInfo.Rendering;

/// <summary>
/// Turns a persisted <c>HwInfo.Sensor</c> command parameter into the <see cref="SensorRow"/> a tile
/// draws. The parameter is the stable HWiNFO triple <c>SensorId:SensorInstance:ReadingId</c>
/// (<see cref="HwInfoSensorRef"/>), unchanged from before the pixel tiles, so saved buttons keep
/// resolving. Values, units, history and alert state come from the <see cref="TelemetrySampler"/>,
/// which tracks every reading under that same reference.
/// </summary>
internal static class HwInfoReadingBuilder
{
    private const string Fallback = "HWiNFO";

    public static SensorRow Build(string? parameter, IReadOnlyList<HwInfoSensor> sensors)
    {
        if (!HwInfoSensorRef.TryParse(parameter, out (uint, uint, uint) id)
            || !ByReference(sensors).TryGetValue(id, out HwInfoSensor? sensor))
            return Placeholder(Fallback);

        string key = MetricKeys.ForSensor(sensor);

        // The menu's name for the reading; a reading the menu does not offer keeps its own label.
        if (TileLabels.For(sensors, key) is { } labels)
            return new SensorRow(labels.Header, labels.Short, key);

        string header = Header(sensor);
        return new SensorRow(header, ShortHeaderFrom(header), key);
    }

    private static SensorRow Placeholder(string header) => new(header, header, null);

    /// <summary>The readings by their HWiNFO triple. Indexed once per sensor snapshot, so a lookup
    /// does not walk every reading.</summary>
    private static Dictionary<(uint, uint, uint), HwInfoSensor> ByReference(IReadOnlyList<HwInfoSensor> sensors)
    {
        SensorIndex? index = _index;
        if (index is null || !ReferenceEquals(index.Sensors, sensors))
        {
            Dictionary<(uint, uint, uint), HwInfoSensor> byReference = new(sensors.Count);
            foreach (HwInfoSensor sensor in sensors)
                byReference.TryAdd((sensor.SensorId, sensor.SensorInstance, sensor.ReadingId), sensor);

            _index = index = new SensorIndex(sensors, byReference);
        }

        return index.ByReference;
    }

    private sealed record SensorIndex(
        IReadOnlyList<HwInfoSensor> Sensors,
        Dictionary<(uint, uint, uint), HwInfoSensor> ByReference);

    // Replaced as a whole when the snapshot changes; render threads may race to build it, which
    // only costs a duplicate build.
    private static volatile SensorIndex? _index;

    /// <summary>The tile / row title for a reading: its HWiNFO label, falling back to the parent
    /// sensor name and finally a reading-id marker.</summary>
    private static string Header(HwInfoSensor sensor)
    {
        if (!string.IsNullOrWhiteSpace(sensor.Label))
            return sensor.Label;
        if (!string.IsNullOrWhiteSpace(sensor.SensorName))
            return sensor.SensorName;
        return $"#{sensor.ReadingId}";
    }

    /// <summary>Compact form of a header for a row of a multi-reading tile: drops a leading
    /// "CPU "/"GPU " so e.g. "CPU Core 7" reads "Core 7".</summary>
    private static string ShortHeaderFrom(string header)
    {
        if ((header.StartsWith("CPU ", StringComparison.Ordinal) || header.StartsWith("GPU ", StringComparison.Ordinal))
            && header.Length > 4)
            return header[4..];

        return header;
    }
}
