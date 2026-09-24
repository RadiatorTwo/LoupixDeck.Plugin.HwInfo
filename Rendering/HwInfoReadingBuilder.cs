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
        if (!HwInfoSensorRef.TryParse(parameter, out string key))
            return Placeholder(Fallback);

        HwInfoSensor? sensor = sensors.FirstOrDefault(s => MetricKeys.ForSensor(s) == key);
        if (sensor is null)
            return Placeholder(Fallback);

        // The menu's name for the reading; a reading the menu does not offer keeps its own label.
        if (TileLabels.For(sensors, key) is { } labels)
            return new SensorRow(labels.Header, labels.Short, key);

        string header = Header(sensor);
        return new SensorRow(header, ShortHeaderFrom(header), key);
    }

    private static SensorRow Placeholder(string header) => new(header, header, null);

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
