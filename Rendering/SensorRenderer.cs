using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.HwInfo.Rendering;

/// <summary>
/// Draws a monitoring tile onto a host <see cref="IRenderCanvas"/> (a touch button is 90×90). Pure
/// drawing: it knows nothing about HWiNFO and only lays out the <see cref="SensorReading"/>s it is
/// handed (1–4). A single reading fills the tile as a header + big value + gauge; two to four stack
/// as rows — header left, value+unit right, a gauge under each row. All sizes derive from
/// <see cref="IRenderCanvas.Width"/>/<see cref="IRenderCanvas.Height"/> and <see cref="SensorTheme"/>.
/// </summary>
public static class SensorRenderer
{
    private const string Ellipsis = "…";

    /// <summary>Hard cap on rows — four gauges fit legibly on a 90×90 tile.</summary>
    public const int MaxReadings = 4;

    public static void Render(IRenderCanvas canvas, IReadOnlyList<SensorReading> readings, SensorTheme theme)
    {
        canvas.Clear(theme.Background);

        int px = theme.Inset;
        int py = theme.Inset;
        int pw = canvas.Width - 2 * theme.Inset;
        int ph = canvas.Height - 2 * theme.Inset;

        if (theme.ShowPanel)
        {
            canvas.FillRoundedRectangle(px, py, pw, ph, theme.CornerRadius, theme.Panel);
            if (theme.BorderWidth > 0)
                canvas.DrawRoundedRectangle(px, py, pw, ph, theme.CornerRadius, theme.BorderWidth, theme.Border);
        }

        int cx = px + theme.Padding;
        int cy = py + theme.Padding;
        int cw = pw - 2 * theme.Padding;
        int ch = ph - 2 * theme.Padding;
        if (cw < 1 || ch < 1)
            return;

        int n = Math.Min(readings.Count, MaxReadings);
        if (n == 0)
            return;

        if (n == 1)
            RenderSingle(canvas, readings[0], theme, cx, cy, cw, ch);
        else
            RenderRows(canvas, readings, n, theme, cx, cy, cw, ch);
    }

    /// <summary>One reading: centered header band, a large value+unit filling the middle and a
    /// gauge at the bottom.</summary>
    private static void RenderSingle(IRenderCanvas canvas, SensorReading r, SensorTheme theme,
        int x, int y, int w, int h)
    {
        int headerH = 0;
        if (!string.IsNullOrWhiteSpace(r.Header))
        {
            headerH = (int)theme.HeaderFontSize + 4;
            string header = Fit(canvas, r.Header, theme.HeaderFontSize, w, bold: false);
            canvas.DrawText(header, x, y, w, headerH, theme.HeaderColor, theme.HeaderFontSize,
                TextHAlign.Center, TextVAlign.Middle, outlined: theme.OutlineText, outlineColor: theme.OutlineColor);
        }

        const int gap = 3;
        bool hasBar = theme.ShowBar && r.Fraction.HasValue;
        int barH = hasBar ? theme.BarHeight : 0;

        int bodyY = y + headerH;
        int bodyH = h - headerH - (hasBar ? barH + theme.BarBottomGap + gap : 0);
        if (bodyH < 1)
            return;

        DrawValueUnit(canvas, x, bodyY, w, bodyH, r.Value, r.Unit,
            theme.ValueFontSize, theme.UnitFontSize, theme.ValueColor, theme.UnitColor, theme.OutlineText, theme.OutlineColor);

        if (hasBar)
        {
            int barX = x + theme.BarInsetX;
            int barW = Math.Max(1, w - 2 * theme.BarInsetX);
            DrawBar(canvas, barX, y + h - barH - theme.BarBottomGap, barW, barH, r.Fraction!.Value, theme, r.Accent ?? theme.BarFill);
        }
    }

    /// <summary>Two to four readings as stacked rows: header label left, value+unit right, a gauge
    /// under each row. The row font is capped so it stays legible instead of growing to fill the row.</summary>
    private static void RenderRows(IRenderCanvas canvas, IReadOnlyList<SensorReading> readings, int n,
        SensorTheme theme, int x, int y, int w, int h)
    {
        // Reserve bottom clearance so the lowest gauge is not clipped by the device bezel.
        int usable = Math.Max(1, h - theme.BarBottomGap);
        int rowH = Math.Max(1, usable / n);

        // Widest label / value across the rows at a given font — used to fit both columns.
        const int colGap = 4;
        float WidestLabel(float f)
        {
            float m = 0f;
            for (int i = 0; i < n; i++)
                m = Math.Max(m, canvas.MeasureText(RowLabel(readings[i]), f));
            return m;
        }
        float WidestValue(float f)
        {
            float m = 0f;
            for (int i = 0; i < n; i++)
                m = Math.Max(m, canvas.MeasureText(ValueText(readings[i]), f));
            return m;
        }

        // Start from the row height, cap at the row font, then shrink until the widest label and the
        // widest value fit side by side. The +2 is a safety margin — MeasureText slightly under-counts
        // the rendered width, so without it the check passes at a font that actually overflows.
        float rowFont = Math.Clamp(rowH * 0.5f, 8f, theme.RowFontSize);
        while (rowFont > 8f && WidestLabel(rowFont) + WidestValue(rowFont) + colGap + 2f > w)
            rowFont -= 0.5f;

        // Size the value column to the widest value so the value is never clipped; the label takes the
        // rest (the shrink above guarantees that leftover still covers the widest label).
        int valueColW = Math.Min((int)Math.Ceiling(WidestValue(rowFont)) + 2, Math.Max(1, w - 8));
        int labelColW = Math.Max(1, w - valueColW);

        for (int i = 0; i < n; i++)
        {
            SensorReading r = readings[i];
            int rowY = y + i * rowH;

            bool hasBar = theme.ShowBar && r.Fraction.HasValue;
            int barH = hasBar ? Math.Clamp(rowH - (int)rowFont - 3, 3, theme.BarHeight) : 0;
            int textH = rowH - (hasBar ? barH + 1 : 0);
            if (textH < 1)
                textH = rowH;

            canvas.DrawText(Fit(canvas, RowLabel(r), rowFont, labelColW, false),
                x, rowY, labelColW, textH, theme.CaptionColor, rowFont, TextHAlign.Left, TextVAlign.Middle,
                outlined: theme.OutlineText, outlineColor: theme.OutlineColor);

            int valueX = x + labelColW;
            int valueW = w - labelColW;
            canvas.DrawText(Fit(canvas, ValueText(r), rowFont, valueW, false),
                valueX, rowY, valueW, textH, theme.RowColor, rowFont, TextHAlign.Right, TextVAlign.Middle,
                outlined: theme.OutlineText, outlineColor: theme.OutlineColor);

            if (hasBar)
            {
                int barX = x + theme.BarInsetX;
                int barW = Math.Max(1, w - 2 * theme.BarInsetX);
                DrawBar(canvas, barX, rowY + textH, barW, barH, r.Fraction!.Value, theme, r.Accent ?? theme.BarFill);
            }
        }
    }

    /// <summary>The label to show for a reading in row mode: its compact header when set, else the
    /// full header.</summary>
    private static string RowLabel(SensorReading r) =>
        string.IsNullOrEmpty(r.ShortHeader) ? r.Header : r.ShortHeader!;

    /// <summary>The value + unit shown on the right of a row (unit omitted when empty).</summary>
    private static string ValueText(SensorReading r) =>
        string.IsNullOrEmpty(r.Unit) ? r.Value : $"{r.Value} {r.Unit}";

    /// <summary>Draws a horizontal gauge: a full-width track with an accent fill proportional to
    /// <paramref name="fraction"/> (clamped 0..1).</summary>
    private static void DrawBar(IRenderCanvas canvas, int x, int y, int w, int h, double fraction,
        SensorTheme theme, PluginColor fill)
    {
        int radius = Math.Min(theme.BarRadius, h / 2);
        canvas.FillRoundedRectangle(x, y, w, h, radius, theme.BarTrack);

        int fillW = (int)Math.Round(w * Math.Clamp(fraction, 0.0, 1.0));
        if (fillW > 0)
            canvas.FillRoundedRectangle(x, y, fillW, h, Math.Min(radius, fillW / 2), fill);
    }

    /// <summary>
    /// Draws a large value with a smaller unit beside it, the pair centered horizontally within the
    /// box and vertically middled. The value font is shrunk to fit the available width; the unit sits
    /// slightly above the value's vertical center per the design.
    /// </summary>
    private static void DrawValueUnit(IRenderCanvas canvas, int x, int y, int w, int h,
        string value, string unit, float valueFont, float unitFont, PluginColor valueColor, PluginColor unitColor,
        bool outline, PluginColor outlineColor)
    {
        if (string.IsNullOrEmpty(value))
            return;

        bool hasUnit = !string.IsNullOrEmpty(unit);
        const int gap = 3;
        float unitW = hasUnit ? canvas.MeasureText(unit, unitFont) : 0f;

        // Shrink the value font until value + gap + unit fits the width.
        float fitFont = valueFont;
        float numW = canvas.MeasureText(value, fitFont, bold: true);
        while (fitFont > 10f && numW + (hasUnit ? gap + unitW : 0f) > w)
        {
            fitFont -= 1f;
            numW = canvas.MeasureText(value, fitFont, bold: true);
        }

        float total = numW + (hasUnit ? gap + unitW : 0f);
        int startX = x + (int)Math.Max(0f, (w - total) / 2f);

        canvas.DrawText(value, startX, y, (int)Math.Ceiling(numW) + 1, h, valueColor, fitFont,
            TextHAlign.Left, TextVAlign.Middle, bold: true, outlined: outline, outlineColor: outlineColor);

        if (hasUnit)
        {
            // Nudge the unit up so it reads as a superscript-ish unit next to the number.
            int unitY = y - (int)(fitFont * 0.12f);
            canvas.DrawText(unit, startX + (int)Math.Ceiling(numW) + gap, unitY, (int)Math.Ceiling(unitW) + 1, h,
                unitColor, unitFont, TextHAlign.Left, TextVAlign.Middle, outlined: outline, outlineColor: outlineColor);
        }
    }

    /// <summary>Truncates text with a trailing ellipsis so it fits <paramref name="maxWidth"/>.</summary>
    private static string Fit(IRenderCanvas canvas, string text, float fontSize, float maxWidth, bool bold)
    {
        if (string.IsNullOrEmpty(text) || canvas.MeasureText(text, fontSize, bold) <= maxWidth)
            return text;

        string trimmed = text;
        while (trimmed.Length > 1 && canvas.MeasureText(trimmed + Ellipsis, fontSize, bold) > maxWidth)
            trimmed = trimmed[..^1];

        return trimmed + Ellipsis;
    }
}
