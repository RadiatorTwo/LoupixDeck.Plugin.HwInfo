using LoupixDeck.Plugin.HwInfo.Rendering.Pixel;
using LoupixDeck.Plugin.HwInfo.Telemetry;

namespace LoupixDeck.Plugin.HwInfo.Rendering.Tiles;

/// <summary>
/// Building blocks shared by the tile layouts, ported from the design prototype (hw-screen.js):
/// state colors, the header band, the hero value, the gauge bar and the history chart. All
/// coordinates are design coordinates (content grid <see cref="PixelSurface.Left"/>..
/// <see cref="PixelSurface.Right"/>).
/// </summary>
internal static class TileDrawing
{
    public const int L = PixelSurface.Left;
    public const int R = PixelSurface.Right;
    public const int W = PixelSurface.ContentSize;

    /// <summary>Header band: rows 8–16 across the 74-px window.</summary>
    public const int HeaderX = 8;
    public const int HeaderY = 8;
    public const int HeaderWidth = 74;
    public const int HeaderHeight = 9;

    /// <summary>The blink phase of a critical alert (1 Hz, on for 500 ms).</summary>
    public static bool BlinkOn(TimeSpan elapsed) => ((long)(elapsed.TotalMilliseconds / 500) % 2) == 0;

    public static uint StateColor(PixelPalette palette, MetricState state) => state switch
    {
        MetricState.Critical => palette.Critical,
        MetricState.Warn => palette.Warn,
        _ => palette.Text
    };

    public static MetricState Worst(IEnumerable<MetricSnapshot?> metrics)
    {
        MetricState worst = MetricState.Ok;
        foreach (MetricSnapshot? metric in metrics)
        {
            if (metric is not null && metric.State > worst)
                worst = metric.State;
        }

        return worst;
    }

    /// <summary>
    /// The header band with <paramref name="title"/> left and <paramref name="index"/> right. Warn
    /// fills it amber; critical fills it red on the blink's on phase and frames it in red on the off
    /// phase — colour is never the only cue, and a critical header shows "!" instead of the index.
    /// </summary>
    public static void Header(PixelSurface surface, string title, string? index, MetricState worst, bool blinkOn)
    {
        PixelPalette p = surface.Palette;
        bool alert = worst != MetricState.Ok;
        bool filled = worst == MetricState.Warn || (worst == MetricState.Critical && blinkOn);

        uint background = alert ? (filled ? StateColor(p, worst) : p.Background) : p.Track;
        uint text = alert ? (filled ? p.Ink : p.Critical) : p.Text;

        surface.Fill(HeaderX, HeaderY, HeaderWidth, HeaderHeight, background);
        if (alert && !filled)
        {
            surface.Fill(HeaderX, HeaderY, HeaderWidth, 1, p.Critical);
            surface.Fill(HeaderX, HeaderY + HeaderHeight - 1, HeaderWidth, 1, p.Critical);
        }

        string right = alert ? "!" : index ?? string.Empty;
        int titleRoom = W - 2 - (right.Length > 0 ? PixelFont.Measure(right) + 3 : 0);
        surface.Text(Fit(title, titleRoom), L + 1, 9, text, shadow: !filled);
        if (right.Length > 0)
            surface.TextRight(right, R - 1, 9, text, shadow: !filled);
    }

    /// <summary>
    /// Draws a value at the largest scale (≤ <paramref name="maxScale"/>) that leaves room for a
    /// <paramref name="sideWidth"/>-px column to its right, and returns where that column starts.
    /// </summary>
    public static (int SideX, int Scale) HeroValue(PixelSurface surface, string value, int y, uint color,
        int maxScale, int sideWidth)
    {
        int scale = maxScale;
        while (scale > 1 && PixelFont.Measure(value, scale) + 3 + sideWidth > W)
            scale--;

        surface.Text(value, L, y, color, scale);
        return (L + PixelFont.Measure(value, scale) + 3, scale);
    }

    /// <summary>Gauge bar across the content width, with a 1-px amber tick at the warn limit.</summary>
    public static void Bar(PixelSurface surface, MetricSnapshot metric, int y, int height)
    {
        PixelPalette p = surface.Palette;
        surface.Fill(L, y, W, height, p.Track);
        uint fill = metric.State == MetricState.Ok ? p.Accent : StateColor(p, metric.State);
        surface.Fill(L, y, Math.Max(1, (int)Math.Round(W * metric.Fraction)), height, fill);

        if (metric.WarnAt is { } warn && metric.HasRange)
        {
            int x = L + (int)Math.Round(W * (warn - metric.Min) / (metric.Max - metric.Min));
            if (x >= L && x <= R)
                surface.Fill(x, y - 1, 1, height + 2, p.Warn);
        }
    }

    /// <summary>
    /// History chart, one column per second, newest at the right edge. Auto-scaled, but never to
    /// less than 12 % of the metric's range (or of its peak when it has none), so sensor noise
    /// doesn't look like a spike. Missing samples leave a gap.
    /// </summary>
    public static void Spark(PixelSurface surface, MetricSnapshot metric, int y, int height)
    {
        if (height < 2)
            return;

        double[] history = metric.History;
        int count = Math.Min(history.Length, W);
        int first = history.Length - count;

        double lo = double.MaxValue;
        double hi = double.MinValue;
        for (int i = first; i < history.Length; i++)
        {
            if (double.IsNaN(history[i]))
                continue;
            lo = Math.Min(lo, history[i]);
            hi = Math.Max(hi, history[i]);
        }

        if (lo > hi)
            return;

        double minSpan = metric.HasRange ? (metric.Max - metric.Min) * 0.12 : Math.Abs(hi) * 0.12;
        minSpan = Math.Max(minSpan, 1e-9);
        if (hi - lo < minSpan)
        {
            if (metric.HasRange)
            {
                double mid = (hi + lo) / 2;
                lo = mid - (minSpan / 2);
            }

            hi = lo + minSpan;
        }

        PixelPalette p = surface.Palette;
        int x0 = L + (W - count);
        for (int i = 0; i < count; i++)
        {
            double value = history[first + i];
            if (double.IsNaN(value))
                continue;

            int barHeight = Math.Max(1, (int)Math.Round((value - lo) / (hi - lo) * (height - 1)) + 1);
            barHeight = Math.Min(barHeight, height);
            surface.Fill(x0 + i, y + height - barHeight, 1, barHeight, p.Spark);
            surface.Fill(x0 + i, y + height - barHeight, 1, 1, p.Accent);
        }
    }

    /// <summary>Cuts <paramref name="text"/> from the end until it fits <paramref name="maxWidth"/>
    /// at <paramref name="scale"/>.</summary>
    public static string Fit(string text, int maxWidth, int scale = 1, int spacing = -1)
    {
        string fitted = text.Trim();
        while (fitted.Length > 0 && PixelFont.Measure(fitted, scale, spacing) > maxWidth)
            fitted = fitted[..^1].TrimEnd();

        return fitted;
    }

    /// <summary>The "HWiNFO is not running" screen (or its shared memory is off).</summary>
    public static void Unavailable(PixelSurface surface, string title) =>
        Placeholder(surface, title, "HWINFO", "NOT RUNNING");

    /// <summary>HWiNFO runs but reports nothing for this tile (e.g. no network adapter).</summary>
    public static void NoData(PixelSurface surface, string title) =>
        Placeholder(surface, title, "NO DATA", string.Empty);

    private static void Placeholder(PixelSurface surface, string title, string line1, string line2)
    {
        PixelPalette p = surface.Palette;
        Header(surface, title, null, MetricState.Ok, blinkOn: true);
        surface.Text("N/A", L, 21, p.Dim, 3);
        surface.Text(line1, L, 52, p.Dim);
        surface.Text(line2, L, 62, p.Dim);
    }
}
