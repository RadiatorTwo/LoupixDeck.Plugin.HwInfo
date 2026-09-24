using LoupixDeck.Plugin.HwInfo.Rendering.Pixel;
using LoupixDeck.Plugin.HwInfo.Telemetry;

namespace LoupixDeck.Plugin.HwInfo.Rendering.Tiles;

/// <summary>
/// Draws a <see cref="ComponentPage"/>. Page anatomy (design y): header 8–16, hero 21–41,
/// bar 45–47, rows at 52 / 62 / 72, history chart in whatever rows are left. The summary page is
/// the four-row grid of layout B.
/// </summary>
internal static class PageLayout
{
    private const int HeroY = 21;
    private const int BarY = 45;
    private const int BarHeight = 3;
    private const int FirstRowY = 52;
    private const int RowPitch = 10;
    private const int MaxRows = 3;

    /// <param name="pageIndex">"n/N" for the header, or null on a tile that shows a single page.</param>
    public static void Draw(PixelSurface surface, ComponentPage page, string? pageIndex, TelemetryFrame frame,
        bool blinkOn)
    {
        if (page.IsSummary)
        {
            DrawSummary(surface, page, frame, blinkOn);
            return;
        }

        PixelPalette p = surface.Palette;
        MetricSnapshot? hero = frame.Get(page.Hero);
        List<MetricSnapshot?> rows = page.Rows.Take(MaxRows).Select(r => frame.Get(r.Metric)).ToList();

        MetricState worst = TileDrawing.Worst(rows.Prepend(hero));
        TileDrawing.Header(surface, page.Title, pageIndex, worst, blinkOn);

        if (hero is null)
        {
            surface.Text("--", TileDrawing.L, HeroY, p.Dim, 3);
        }
        else
        {
            (string value, string unit) = hero.Formatted;
            int side = Math.Max(PixelFont.Measure(unit), PixelFont.Measure(page.HeroLabel));
            (int sideX, int scale) = TileDrawing.HeroValue(surface, value, HeroY,
                TileDrawing.StateColor(p, hero.State), 3, side);
            surface.Text(unit, sideX, HeroY, p.Dim);
            surface.Text(page.HeroLabel, sideX, HeroY + (scale == 3 ? 12 : 8), p.Dim);

            if (hero.HasRange)
                TileDrawing.Bar(surface, hero, BarY, BarHeight);
        }

        for (int i = 0; i < rows.Count; i++)
            DrawRow(surface, page.Rows[i].Label, rows[i], FirstRowY + (RowPitch * i));

        if (rows.Count < MaxRows && page.Spark is not null && frame.Get(page.Spark) is { } spark)
        {
            int y = FirstRowY + (RowPitch * rows.Count);
            TileDrawing.Spark(surface, spark, y, PixelSurface.Bottom + 1 - y);
        }
    }

    /// <summary>A 1× row: dim label left, value and unit right; an alerting row starts with "!".</summary>
    public static void DrawRow(PixelSurface surface, string label, MetricSnapshot? metric, int y)
    {
        PixelPalette p = surface.Palette;
        string text;
        uint color;
        if (metric is null)
        {
            text = "--";
            color = p.Dim;
        }
        else
        {
            (string value, string unit) = metric.Formatted;
            text = (metric.State != MetricState.Ok ? "! " : string.Empty) + value + unit;
            color = TileDrawing.StateColor(p, metric.State);
        }

        int valueWidth = PixelFont.Measure(text);
        surface.Text(TileDrawing.Fit(label, TileDrawing.W - valueWidth - 3), TileDrawing.L, y, p.Dim);
        surface.TextRight(text, TileDrawing.R, y, color);
    }

    /// <summary>Row pitch of the grid without and with a gauge bar under each row.</summary>
    public const int GridPitch = 18;
    public const int BarGridPitch = 31;

    /// <summary>Offset of the gauge bar below a row's top, and its height.</summary>
    private const int GridBarOffset = 18;
    private const int GridBarHeight = 3;

    /// <summary>Height of a grid of <paramref name="count"/> rows, from the first row's top to the
    /// last drawn pixel.</summary>
    public static int GridHeight(int count, bool bars) => bars
        ? (BarGridPitch * (count - 1)) + GridBarOffset + GridBarHeight
        : (GridPitch * count) - 2;

    /// <summary>
    /// Layout B: up to four 18-px rows, label and unit stacked left at 1×, value right at 2× with
    /// 1-px spacing. An alerting row is filled (warn, critical on) or framed (critical off). With
    /// <paramref name="bars"/> each row gets a gauge bar below it (rows with a known range only)
    /// and the rows move further apart — for tiles with few rows, where the space is there.
    /// </summary>
    public static void DrawGrid(PixelSurface surface, IReadOnlyList<(string Label, MetricSnapshot? Metric)> rows,
        bool blinkOn, int top = PixelSurface.Top, bool bars = false)
    {
        PixelPalette p = surface.Palette;
        for (int i = 0; i < rows.Count; i++)
        {
            (string label, MetricSnapshot? metric) = rows[i];
            int y0 = top + ((bars ? BarGridPitch : GridPitch) * i);
            MetricState state = metric?.State ?? MetricState.Ok;
            bool fill = state != MetricState.Ok && (state == MetricState.Warn || blinkOn);

            if (fill)
            {
                surface.Fill(TileDrawing.HeaderX, y0 - 1, TileDrawing.HeaderWidth, 18, TileDrawing.StateColor(p, state));
            }
            else if (state != MetricState.Ok)
            {
                surface.Fill(TileDrawing.HeaderX, y0 - 1, TileDrawing.HeaderWidth, 1, p.Critical);
                surface.Fill(TileDrawing.HeaderX, y0 + 16, TileDrawing.HeaderWidth, 1, p.Critical);
            }

            (string value, string unit) = metric?.Formatted ?? ("--", string.Empty);
            uint labelColor = fill ? p.Ink : p.Dim;
            uint valueColor = fill ? p.Ink : metric is null ? p.Dim : TileDrawing.StateColor(p, state);

            // Value at 2× when it leaves the label column readable, else at 1×.
            int labelWidth = Math.Min(PixelFont.Measure(label), 30);
            int scale = PixelFont.Measure(value, 2, 1) <= TileDrawing.W - 2 - labelWidth - 3 ? 2 : 1;
            int spacing = scale == 2 ? 1 : -1;
            int valueWidth = PixelFont.Measure(value, scale, spacing);

            int labelRoom = TileDrawing.W - 1 - valueWidth - 3;
            surface.Text(TileDrawing.Fit(label, labelRoom), TileDrawing.L + 1, y0 + 1, labelColor, shadow: !fill);
            surface.Text(TileDrawing.Fit(unit, labelRoom), TileDrawing.L + 1, y0 + 9, labelColor, shadow: !fill);
            surface.TextRight(value, TileDrawing.R - 1, y0 + 1, valueColor, scale, spacing, shadow: !fill);

            if (bars)
            {
                if (metric is { HasRange: true })
                    TileDrawing.Bar(surface, metric, y0 + GridBarOffset, GridBarHeight);
            }
            else if (i < rows.Count - 1 && state == MetricState.Ok)
            {
                surface.Fill(TileDrawing.L, y0 + 16, TileDrawing.W, 1, p.Track);
            }
        }
    }

    private static void DrawSummary(PixelSurface surface, ComponentPage page, TelemetryFrame frame, bool blinkOn) =>
        DrawGrid(surface, page.Rows.Take(4).Select(r => (r.Label, frame.Get(r.Metric))).ToList(), blinkOn);
}
