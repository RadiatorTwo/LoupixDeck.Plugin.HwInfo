using System.Globalization;

namespace LoupixDeck.Plugin.HwInfo.Telemetry;

/// <summary>How a metric's native value is turned into the text drawn on a tile.</summary>
internal enum MetricFormat
{
    /// <summary>Number with at most one decimal plus the sensor's own unit.</summary>
    Generic,
    Temperature,
    Percent,
    /// <summary>Native MHz, shown as GHz with two decimals ("4.62 GHZ").</summary>
    ClockMhz,
    Rpm,
    Watt,
    /// <summary>Native MB, shown as GB with one decimal ("19.8 G").</summary>
    Megabytes,
    /// <summary>Native bytes per second, scaled to KB/S, MB/S or GB/S.</summary>
    BytesPerSecond,
    /// <summary>A bare multiplier ("44.5").</summary>
    Multiplier
}

/// <summary>Formats metric values for the pixel font: invariant culture, uppercase-safe units.</summary>
internal static class MetricFormatter
{
    /// <summary>Formats <paramref name="value"/> (native units) as value text and unit.
    /// <paramref name="genericUnit"/> is the sensor's own unit, used by <see cref="MetricFormat.Generic"/>.</summary>
    public static (string Value, string Unit) Format(double value, MetricFormat format, string? genericUnit = null)
    {
        if (double.IsNaN(value))
            return ("--", string.Empty);

        return format switch
        {
            MetricFormat.Temperature => (Fixed(value, 0), "°C"),
            MetricFormat.Percent => (Fixed(value, 0), "%"),
            MetricFormat.ClockMhz => (Fixed(value / 1000.0, 2), "GHZ"),
            MetricFormat.Rpm => (Fixed(value, 0), "RPM"),
            MetricFormat.Watt => (Fixed(value, 0), "W"),
            MetricFormat.Megabytes => (Fixed(value / 1024.0, 1), "G"),
            MetricFormat.BytesPerSecond => Rate(value),
            MetricFormat.Multiplier => (Fixed(value, 1), "X"),
            _ => (Compact(value), genericUnit ?? string.Empty)
        };
    }

    /// <summary>Converts a transfer rate to bytes per second from a unit string ("Byte/s",
    /// "KB/s", "Mbit/s", "Bytes/sec (up)" …). Null when the unit is not a rate.</summary>
    public static double? ToBytesPerSecond(double value, string? unit)
    {
        string u = (unit ?? string.Empty).ToLowerInvariant();

        // Network rates carry their direction: "Bytes/sec (up)".
        int parenthesis = u.IndexOf('(');
        if (parenthesis >= 0)
            u = u[..parenthesis];

        u = u.Replace(" ", string.Empty);
        if (u.EndsWith("/sec", StringComparison.Ordinal))
            u = u[..^2];
        if (!u.EndsWith("/s", StringComparison.Ordinal))
            return null;

        u = u[..^2];
        bool bits = u.EndsWith("bit", StringComparison.Ordinal);
        string prefix = u.TrimEnd('b', 'y', 't', 'e', 'i', 's');
        double factor = prefix switch
        {
            "" => 1,
            "k" => 1024,
            "m" => 1024 * 1024,
            "g" => 1024.0 * 1024 * 1024,
            _ => double.NaN
        };

        if (double.IsNaN(factor))
            return null;

        return value * factor / (bits ? 8.0 : 1.0);
    }

    private static (string, string) Rate(double bytesPerSecond)
    {
        const double kb = 1024;
        const double mb = kb * 1024;
        const double gb = mb * 1024;

        if (bytesPerSecond >= gb)
            return (Fixed(bytesPerSecond / gb, 1), "GB/S");
        if (bytesPerSecond >= mb)
        {
            double mbs = bytesPerSecond / mb;
            return (Fixed(mbs, mbs >= 100 ? 0 : 1), "MB/S");
        }

        return (Fixed(bytesPerSecond / kb, 0), "KB/S");
    }

    private static string Fixed(double value, int decimals) =>
        value.ToString("F" + decimals, CultureInfo.InvariantCulture);

    /// <summary>One decimal, trailing ".0" dropped (36 °C, 6.6 %).</summary>
    private static string Compact(double value)
    {
        string text = Fixed(value, 1);
        return text.EndsWith(".0", StringComparison.Ordinal) ? text[..^2] : text;
    }
}
