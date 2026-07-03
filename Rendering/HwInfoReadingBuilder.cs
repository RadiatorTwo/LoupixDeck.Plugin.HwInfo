using System.Globalization;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.HwInfo.Rendering;

/// <summary>
/// Turns a persisted <c>HwInfo.Sensor</c> command parameter and a live sensor snapshot into a
/// <see cref="SensorReading"/>. Owns the sensor→display mapping (label, unit scaling, value
/// formatting, gauge scaling, accent) so <see cref="SensorRenderer"/> stays a pure drawing component.
///
/// <para>The model is <b>one reading per command</b>: a parameter is the stable HWiNFO triple
/// <c>&lt;SensorId&gt;:&lt;SensorInstance&gt;:&lt;ReadingId&gt;</c> and yields a single reading; the
/// user composes a multi-sensor button by chaining several commands (the renderer lays them out as
/// rows). The parameter format is unchanged from the former text command, so buttons saved before the
/// rework keep resolving.</para>
/// </summary>
public static class HwInfoReadingBuilder
{
    /// <summary>Builds the single reading referenced by <paramref name="parameter"/>, or a
    /// placeholder reading when HWiNFO is unavailable, the reference is empty/malformed, or the
    /// sensor is not currently present in the snapshot.</summary>
    public static SensorReading Build(string? parameter, IReadOnlyList<HwInfoSensor> sensors, bool isAvailable)
    {
        if (!isAvailable)
            return Placeholder("HWiNFO", "N/A");

        if (string.IsNullOrWhiteSpace(parameter))
            return Placeholder("HWiNFO", "?");

        if (!TryParseSensorRef(parameter, out uint sensorId, out uint sensorInstance, out uint readingId))
            return Placeholder("HWiNFO", "?");

        HwInfoSensor? sensor = null;
        foreach (HwInfoSensor candidate in sensors)
        {
            if (candidate.SensorId == sensorId && candidate.SensorInstance == sensorInstance && candidate.ReadingId == readingId)
            {
                sensor = candidate;
                break;
            }
        }

        if (sensor is null)
            return Placeholder("HWiNFO", "?");

        (string value, string unit) = Format(sensor.Value, sensor.Unit);
        string header = Header(sensor);
        return new SensorReading(header, value, unit, Fraction(sensor), Accent(sensor.Type), ShortHeaderFrom(header));
    }

    // ── Reference parsing ─────────────────────────────────────────────────────

    // Reference format: "sensorId:sensorInstance:readingId" — the triple HWiNFO keeps stable across
    // runs. Values may be decimal or 0x-prefixed hex. Kept identical to the former text command so old
    // buttons keep resolving.
    private static bool TryParseSensorRef(string raw, out uint sensorId, out uint sensorInstance, out uint readingId)
    {
        sensorId = 0;
        sensorInstance = 0;
        readingId = 0;

        string[] parts = raw.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
            return false;

        return TryParseUInt(parts[0], out sensorId)
               && TryParseUInt(parts[1], out sensorInstance)
               && TryParseUInt(parts[2], out readingId);
    }

    private static bool TryParseUInt(string text, out uint value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return uint.TryParse(text, out value);
    }

    // ── Formatting ────────────────────────────────────────────────────────────

    /// <summary>Formats a value + unit the same way the Argus plugin does, so both plugins render an
    /// identical look: memory MB/GB collapse to a compact "G", and a four-digit MHz clock becomes GHz
    /// (e.g. 4200 MHz → 4.2 GHz) so it stays compact on the tile.</summary>
    private static (string value, string unit) Format(double value, string? rawUnit)
    {
        string unit = rawUnit ?? string.Empty;

        if (unit.Equals("MB", StringComparison.OrdinalIgnoreCase))
        {
            value /= 1024.0;
            unit = "G";
        }
        else if (unit.Equals("GB", StringComparison.OrdinalIgnoreCase))
        {
            unit = "G";
        }
        else if (unit.Equals("MHz", StringComparison.OrdinalIgnoreCase) && Math.Abs(value) >= 1000.0)
        {
            value /= 1000.0;
            unit = "GHz";
        }

        return (FormatNumber(value), unit);
    }

    /// <summary>One decimal place, but a trailing ".0" is dropped so whole numbers read as integers
    /// (36 °C) while fractional readings keep their decimal (6.6 %).</summary>
    private static string FormatNumber(double value)
    {
        string text = value.ToString("F1", CultureInfo.InvariantCulture);
        if (text.EndsWith(".0", StringComparison.Ordinal))
            text = text[..^2];
        return text;
    }

    // ── Gauge fill ──────────────────────────────────────────────────────────────

    // Nominal full-scale values for readings with no natural 0..100 range, matching Argus so the bars
    // fill comparably. Percentages ignore these (they are already 0..100).
    private const double TempMaxC = 100.0;
    private const double PowerMaxW = 100.0;
    private const double FanRpmMax = 3000.0;
    private const double FreqMaxDefaultMhz = 6000.0;

    /// <summary>Returns the 0..1 gauge fill for a reading, or null when it has no meaningful scale (so
    /// no bar is drawn). Percentages use their value directly; temperature, power, fan and clock
    /// readings divide by a nominal per-type maximum (as Argus does) so most tiles carry a bar.
    /// Voltage, current and "other" readings have no natural full-scale, so they draw no bar.</summary>
    private static double? Fraction(HwInfoSensor sensor)
    {
        double? max = MaxFor(sensor);
        if (max is null or <= 0)
            return null;

        return Math.Clamp(sensor.Value / max.Value, 0.0, 1.0);
    }

    private static double? MaxFor(HwInfoSensor sensor)
    {
        // Anything already expressed in percent is 0..100 regardless of its reading type.
        if ((sensor.Unit ?? string.Empty).Equals("%", StringComparison.OrdinalIgnoreCase))
            return 100.0;

        return sensor.Type switch
        {
            HwInfoReadingType.Temperature => TempMaxC,
            HwInfoReadingType.Usage => 100.0,
            HwInfoReadingType.Power => PowerMaxW,
            HwInfoReadingType.Fan => FanRpmMax,
            HwInfoReadingType.Clock => FreqMaxDefaultMhz,
            _ => null
        };
    }

    // ── Accent (bar tint per reading kind) ─────────────────────────────────────

    private static readonly PluginColor AccentTemp = new(0xC0, 0x76, 0x40);     // muted orange
    private static readonly PluginColor AccentUsage = new(0x57, 0x9E, 0x63);    // muted green
    private static readonly PluginColor AccentClock = new(0x53, 0x6D, 0x9E);    // muted steel blue
    private static readonly PluginColor AccentPower = new(0xA8, 0x5C, 0x5C);    // muted red
    private static readonly PluginColor AccentFan = new(0xB0, 0x92, 0x42);      // muted amber

    /// <summary>Muted accent tint for a reading kind, used only for the gauge fill. Null → the theme's
    /// neutral default bar color.</summary>
    private static PluginColor? Accent(HwInfoReadingType type) => type switch
    {
        HwInfoReadingType.Temperature => AccentTemp,
        HwInfoReadingType.Usage => AccentUsage,
        HwInfoReadingType.Clock => AccentClock,
        HwInfoReadingType.Power => AccentPower,
        HwInfoReadingType.Fan => AccentFan,
        _ => null
    };

    // ── Headers ────────────────────────────────────────────────────────────────

    private static SensorReading Placeholder(string header, string value) =>
        new(header, value, string.Empty);

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

    /// <summary>Compact form of a header for use as a row label when several readings share the tile:
    /// drops a leading "CPU "/"GPU " subsystem word so e.g. "CPU Core 7" fits beside its value as
    /// "Core 7". Returns the header unchanged when there is nothing to drop.</summary>
    private static string ShortHeaderFrom(string header)
    {
        if ((header.StartsWith("CPU ", StringComparison.Ordinal) || header.StartsWith("GPU ", StringComparison.Ordinal))
            && header.Length > 4)
            return header[4..];

        return header;
    }
}
