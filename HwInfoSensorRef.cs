using System.Globalization;

namespace LoupixDeck.Plugin.HwInfo;

/// <summary>
/// A sensor reference is the triple HWiNFO keeps stable across runs,
/// <c>SensorId:SensorInstance:ReadingId</c> (see <see cref="HwInfoSensor"/>). The menu writes it in
/// decimal; a saved reference may also use 0x-prefixed hex parts, which parse to the same key, so
/// buttons saved before keep resolving.
/// </summary>
internal static class HwInfoSensorRef
{
    public static string Format(HwInfoSensor sensor) =>
        Format(sensor.SensorId, sensor.SensorInstance, sensor.ReadingId);

    /// <summary>Parses a saved reference into its canonical (decimal) key.</summary>
    public static bool TryParse(string? raw, out string key)
    {
        key = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        string[] parts = raw.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || !TryParseUInt(parts[0], out uint sensorId)
            || !TryParseUInt(parts[1], out uint sensorInstance)
            || !TryParseUInt(parts[2], out uint readingId))
            return false;

        key = Format(sensorId, sensorInstance, readingId);
        return true;
    }

    private static string Format(uint sensorId, uint sensorInstance, uint readingId) =>
        string.Create(CultureInfo.InvariantCulture, $"{sensorId}:{sensorInstance}:{readingId}");

    private static bool TryParseUInt(string text, out uint value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return uint.TryParse(text, out value);
    }
}
