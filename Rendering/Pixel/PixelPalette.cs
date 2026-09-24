using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.HwInfo.Rendering.Pixel;

/// <summary>
/// The colors of the hardware-display design, as packed ARGB (0xAARRGGBB) so the framebuffer can
/// compare pixels as integers. A value with alpha 0 means "leave the pixel empty".
/// </summary>
internal sealed record PixelPalette
{
    /// <summary>Fill behind everything; transparent in the see-through theme.</summary>
    public required uint Background { get; init; }

    /// <summary>Values and other primary text.</summary>
    public required uint Text { get; init; }

    /// <summary>Labels and units.</summary>
    public required uint Dim { get; init; }

    /// <summary>Unfilled part of bars, the resting header band and row separators.</summary>
    public required uint Track { get; init; }

    /// <summary>Bar fill and the top line of a history chart.</summary>
    public required uint Accent { get; init; }

    /// <summary>Body of a history chart.</summary>
    public required uint Spark { get; init; }

    public required uint Warn { get; init; }

    public required uint Critical { get; init; }

    /// <summary>Text drawn on a filled (warn or critical) band.</summary>
    public required uint Ink { get; init; }

    /// <summary>A 1-px drop shadow behind text, or 0 for none. Keeps pixel text legible over a
    /// wallpaper without the soft halo of the host's outlined text.</summary>
    public uint Shadow { get; init; }

    /// <summary>The design's colors on black (report §04).</summary>
    public static PixelPalette Default { get; } = new()
    {
        Background = 0xFF000000,
        Text = 0xFFEDEDED,
        Dim = 0xFF8C8C8C,
        Track = 0xFF262626,
        Accent = 0xFF56C2F5,
        Spark = 0xFF17394A,
        Warn = 0xFFFFB224,
        Critical = 0xFFFF4B3E,
        Ink = 0xFF000000
    };

    /// <summary>See-through variant: no background, text gets a black 1-px shadow.</summary>
    public static PixelPalette Transparent { get; } = Default with
    {
        Background = 0x00000000,
        Shadow = 0xFF000000
    };

    public static PluginColor ToPluginColor(uint argb) =>
        new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
}
