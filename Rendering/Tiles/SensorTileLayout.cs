using LoupixDeck.Plugin.HwInfo.Rendering.Pixel;
using LoupixDeck.Plugin.HwInfo.Telemetry;

namespace LoupixDeck.Plugin.HwInfo.Rendering.Tiles;

/// <summary>
/// Draws a HwInfo.Sensor tile. One reading gets the page anatomy — header, hero value, bar and a
/// history chart below. Two to four readings stack as the 18-px rows of layout B, centred
/// vertically; two readings also get a gauge bar under each row.
/// </summary>
internal static class SensorTileLayout
{
    /// <summary>Four rows of 18 px fill the 72-px grid.</summary>
    public const int MaxRows = 4;

    public static void Draw(PixelSurface surface, IReadOnlyList<SensorRow> rows, TelemetryFrame frame, bool blinkOn)
    {
        if (!frame.IsAvailable)
        {
            TileDrawing.Unavailable(surface, "HWINFO");
            return;
        }

        if (rows.Count == 0)
            return;

        if (rows.Count == 1)
        {
            DrawSingle(surface, rows[0], frame, blinkOn);
            return;
        }

        List<(string, MetricSnapshot?)> grid = rows.Take(MaxRows)
            .Select(r => (r.ShortHeader, Resolve(r, frame)))
            .ToList();
        // Two rows leave room for a gauge bar under each; three and four fill the grid as they are.
        bool bars = grid.Count == 2;
        int blockHeight = PageLayout.GridHeight(grid.Count, bars);
        int top = PixelSurface.Top + ((PixelSurface.ContentSize - blockHeight) / 2);
        PageLayout.DrawGrid(surface, grid, blinkOn, top, bars);
    }

    private static void DrawSingle(PixelSurface surface, SensorRow row, TelemetryFrame frame, bool blinkOn)
    {
        PixelPalette p = surface.Palette;
        MetricSnapshot? metric = Resolve(row, frame);
        TileDrawing.Header(surface, row.Header, null, metric?.State ?? MetricState.Ok, blinkOn);

        if (metric is null)
        {
            surface.Text("?", TileDrawing.L, 21, p.Dim, 3);
            return;
        }

        (string value, string unit) = metric.Formatted;
        (int sideX, _) = TileDrawing.HeroValue(surface, value, 21, TileDrawing.StateColor(p, metric.State), 3,
            PixelFont.Measure(unit));
        surface.Text(unit, sideX, 21, p.Dim);

        int chartTop = 45;
        if (metric.HasRange)
        {
            TileDrawing.Bar(surface, metric, 45, 3);
            chartTop = 52;
        }

        TileDrawing.Spark(surface, metric, chartTop, PixelSurface.Bottom + 1 - chartTop);
    }

    private static MetricSnapshot? Resolve(SensorRow row, TelemetryFrame frame) =>
        row.MetricKey is null ? null : frame.Get(row.MetricKey);
}

/// <summary>
/// One reading on a HwInfo.Sensor tile: the full header (single-reading tile), a compact label
/// (row of a multi-reading tile) and the tracked metric it shows, or null when the referenced
/// sensor does not exist (drawn as "?" / "--").
/// </summary>
internal sealed record SensorRow(string Header, string ShortHeader, string? MetricKey);
