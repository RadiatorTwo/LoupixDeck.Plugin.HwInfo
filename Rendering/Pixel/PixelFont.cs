namespace LoupixDeck.Plugin.HwInfo.Rendering.Pixel;

/// <summary>
/// The 5×7 bitmap font of the hardware-display design. Glyphs are drawn pixel by pixel at whole
/// scales only (1×, 2×, 3×) and never anti-aliased, so every stroke lands on the device grid.
/// Letters are uppercase only — <see cref="Normalize"/> folds lowercase input — and a character
/// without a glyph renders as '?'.
/// </summary>
internal static class PixelFont
{
    /// <summary>Glyph height in font pixels (before scaling).</summary>
    public const int Height = 7;

    /// <summary>One glyph: its width in font pixels and one bit mask per row, the leftmost column
    /// in the highest of <see cref="Width"/> bits.</summary>
    public readonly record struct Glyph(int Width, int[] Rows)
    {
        public bool IsSet(int row, int column) => (Rows[row] & (1 << (Width - 1 - column))) != 0;
    }

    private static readonly Dictionary<char, Glyph> Glyphs = BuildGlyphs();
    private static readonly Glyph Fallback = Glyphs['?'];

    /// <summary>The glyph for <paramref name="c"/> (already normalized), or '?' when the font has
    /// none.</summary>
    public static Glyph For(char c) => Glyphs.TryGetValue(c, out Glyph glyph) ? glyph : Fallback;

    /// <summary>Uppercases <paramref name="text"/> so it can be drawn with this font.</summary>
    public static string Normalize(string text) => text.ToUpperInvariant();

    /// <summary>
    /// Width in device pixels of <paramref name="text"/> drawn at <paramref name="scale"/> with
    /// <paramref name="spacing"/> pixels between glyphs (-1: the scale, the design default).
    /// </summary>
    public static int Measure(string text, int scale = 1, int spacing = -1)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        if (spacing < 0)
            spacing = scale;

        string normalized = Normalize(text);
        int width = 0;
        for (int i = 0; i < normalized.Length; i++)
        {
            width += For(normalized[i]).Width * scale;
            if (i > 0)
                width += spacing;
        }

        return width;
    }

    private static Dictionary<char, Glyph> BuildGlyphs()
    {
        // Rows of the 5-wide glyphs, taken verbatim from the design prototype (hw-screen.js).
        Dictionary<char, int[]> five = new()
        {
            ['0'] = [14, 17, 19, 21, 25, 17, 14], ['1'] = [4, 12, 4, 4, 4, 4, 14],
            ['2'] = [14, 17, 1, 2, 4, 8, 31], ['3'] = [31, 2, 4, 2, 1, 17, 14],
            ['4'] = [2, 6, 10, 18, 31, 2, 2], ['5'] = [31, 16, 30, 1, 1, 17, 14],
            ['6'] = [6, 8, 16, 30, 17, 17, 14], ['7'] = [31, 1, 2, 4, 8, 8, 8],
            ['8'] = [14, 17, 17, 14, 17, 17, 14], ['9'] = [14, 17, 17, 15, 1, 2, 12],
            ['A'] = [14, 17, 17, 17, 31, 17, 17], ['B'] = [30, 17, 17, 30, 17, 17, 30],
            ['C'] = [14, 17, 16, 16, 16, 17, 14], ['D'] = [28, 18, 17, 17, 17, 18, 28],
            ['E'] = [31, 16, 16, 30, 16, 16, 31], ['F'] = [31, 16, 16, 30, 16, 16, 16],
            ['G'] = [14, 17, 16, 23, 17, 17, 15], ['H'] = [17, 17, 17, 31, 17, 17, 17],
            ['I'] = [14, 4, 4, 4, 4, 4, 14], ['J'] = [7, 2, 2, 2, 2, 18, 12],
            ['K'] = [17, 18, 20, 24, 20, 18, 17], ['L'] = [16, 16, 16, 16, 16, 16, 31],
            ['M'] = [17, 27, 21, 21, 17, 17, 17], ['N'] = [17, 17, 25, 21, 19, 17, 17],
            ['O'] = [14, 17, 17, 17, 17, 17, 14], ['P'] = [30, 17, 17, 30, 16, 16, 16],
            ['Q'] = [14, 17, 17, 17, 21, 18, 13], ['R'] = [30, 17, 17, 30, 20, 18, 17],
            ['S'] = [15, 16, 16, 14, 1, 1, 30], ['T'] = [31, 4, 4, 4, 4, 4, 4],
            ['U'] = [17, 17, 17, 17, 17, 17, 14], ['V'] = [17, 17, 17, 17, 17, 10, 4],
            ['W'] = [17, 17, 17, 21, 21, 21, 10], ['X'] = [17, 17, 10, 4, 10, 17, 17],
            ['Y'] = [17, 17, 17, 10, 4, 4, 4], ['Z'] = [31, 1, 2, 4, 8, 16, 31],
            ['/'] = [1, 1, 2, 4, 8, 16, 16], ['%'] = [24, 25, 2, 4, 8, 19, 3],

            // Additions beyond the prototype: punctuation that sensor labels use, and the German
            // umlauts (dots in the top row, letter body in rows 2-6).
            ['?'] = [14, 17, 1, 2, 4, 0, 4], ['+'] = [0, 4, 4, 31, 4, 4, 0],
            ['#'] = [10, 10, 31, 10, 31, 10, 10], ['_'] = [0, 0, 0, 0, 0, 0, 31],
            ['='] = [0, 0, 31, 0, 31, 0, 0], ['<'] = [2, 4, 8, 16, 8, 4, 2],
            ['>'] = [8, 4, 2, 1, 2, 4, 8],
            ['Ä'] = [10, 0, 14, 17, 31, 17, 17], ['Ö'] = [10, 0, 14, 17, 17, 17, 14],
            ['Ü'] = [10, 0, 17, 17, 17, 17, 14]
        };

        Dictionary<char, Glyph> glyphs = [];
        foreach ((char c, int[] rows) in five)
            glyphs[c] = new Glyph(5, rows);

        // Narrow glyphs, as in the prototype.
        glyphs['.'] = new Glyph(1, [0, 0, 0, 0, 0, 0, 1]);
        glyphs[':'] = new Glyph(1, [0, 0, 1, 0, 0, 1, 0]);
        glyphs['!'] = new Glyph(1, [1, 1, 1, 1, 1, 0, 1]);
        glyphs['°'] = new Glyph(3, [2, 5, 2, 0, 0, 0, 0]);
        glyphs['-'] = new Glyph(3, [0, 0, 0, 7, 0, 0, 0]);
        glyphs[' '] = new Glyph(3, [0, 0, 0, 0, 0, 0, 0]);
        glyphs[','] = new Glyph(2, [0, 0, 0, 0, 0, 1, 2]);
        glyphs['('] = new Glyph(3, [1, 2, 4, 4, 4, 2, 1]);
        glyphs[')'] = new Glyph(3, [4, 2, 1, 1, 1, 2, 4]);

        return glyphs;
    }
}
