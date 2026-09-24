namespace LoupixDeck.Plugin.HwInfo.Rendering.Pixel;

/// <summary>
/// A plugin-owned ARGB framebuffer the size of the host canvas, drawn in the coordinates of the
/// reference design: a 90×90 panel whose 72×72 content grid runs from <see cref="Left"/> to
/// <see cref="Right"/>. The grid is centred in whatever canvas the host hands out — 90×90 on the
/// Loupedeck family (offset 0, so design coordinates are canvas coordinates), 74×74 on the Razer
/// Stream Controller X, whose host calibration already crops away the key-cap bezel the design
/// was drawn around. Anything outside the canvas is clipped.
/// </summary>
internal sealed class PixelSurface
{
    /// <summary>Edge of the design's content grid.</summary>
    public const int ContentSize = 72;

    /// <summary>First and last content column/row in design coordinates.</summary>
    public const int Left = 9;
    public const int Right = 80;
    public const int Top = 9;
    public const int Bottom = 80;

    private readonly uint[] _pixels;
    private readonly int _offsetX;
    private readonly int _offsetY;

    public PixelSurface(int width, int height, PixelPalette palette)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Palette = palette;
        _pixels = new uint[Width * Height];
        _offsetX = ((Width - ContentSize) / 2) - Left;
        _offsetY = ((Height - ContentSize) / 2) - Top;
        Array.Fill(_pixels, palette.Background);
    }

    public int Width { get; }

    public int Height { get; }

    public PixelPalette Palette { get; }

    /// <summary>The framebuffer in canvas coordinates, row-major.</summary>
    public ReadOnlySpan<uint> Pixels => _pixels;

    /// <summary>Fills a rectangle given in design coordinates.</summary>
    public void Fill(int x, int y, int width, int height, uint color)
    {
        int left = Math.Max(0, x + _offsetX);
        int top = Math.Max(0, y + _offsetY);
        int right = Math.Min(Width, x + _offsetX + width);
        int bottom = Math.Min(Height, y + _offsetY + height);

        for (int row = top; row < bottom; row++)
            _pixels.AsSpan((row * Width) + left, Math.Max(0, right - left)).Fill(color);
    }

    /// <summary>
    /// Draws <paramref name="text"/> with its top-left at (<paramref name="x"/>,<paramref name="y"/>)
    /// and returns the x just past its last glyph. <paramref name="spacing"/> -1 uses the scale.
    /// The palette's shadow is drawn behind unless <paramref name="shadow"/> is false (text on a
    /// filled band).
    /// </summary>
    public int Text(string text, int x, int y, uint color, int scale = 1, int spacing = -1, bool shadow = true)
    {
        if (string.IsNullOrEmpty(text))
            return x;

        if (spacing < 0)
            spacing = scale;

        string normalized = PixelFont.Normalize(text);
        if (shadow && Palette.Shadow != 0)
            DrawGlyphs(normalized, x + 1, y + 1, Palette.Shadow, scale, spacing);

        return DrawGlyphs(normalized, x, y, color, scale, spacing);
    }

    /// <summary>Draws <paramref name="text"/> so its last pixel column is <paramref name="right"/>.</summary>
    public void TextRight(string text, int right, int y, uint color, int scale = 1, int spacing = -1, bool shadow = true) =>
        Text(text, right + 1 - PixelFont.Measure(text, scale, spacing), y, color, scale, spacing, shadow);

    /// <summary>A 64-bit FNV-1a hash of the frame, made positive and non-zero so it can serve as
    /// the host's dirty key (<c>AnimationFrameInfo.FrameNumber</c>).</summary>
    public long ContentHash()
    {
        ulong hash = 14695981039346656037UL;
        hash = (hash ^ (uint)Width) * 1099511628211UL;
        hash = (hash ^ (uint)Height) * 1099511628211UL;
        foreach (uint pixel in _pixels)
            hash = (hash ^ pixel) * 1099511628211UL;

        return (long)(hash >> 1) | 1L;
    }

    private int DrawGlyphs(string normalized, int x, int y, uint color, int scale, int spacing)
    {
        for (int i = 0; i < normalized.Length; i++)
        {
            if (i > 0)
                x += spacing;

            PixelFont.Glyph glyph = PixelFont.For(normalized[i]);
            for (int row = 0; row < PixelFont.Height; row++)
            {
                for (int column = 0; column < glyph.Width; column++)
                {
                    if (glyph.IsSet(row, column))
                        Fill(x + (column * scale), y + (row * scale), scale, scale, color);
                }
            }

            x += glyph.Width * scale;
        }

        return x;
    }
}
