using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.HwInfo.Rendering;

/// <summary>
/// All colors, font sizes, spacings and border settings of a sensor tile, gathered so the look can
/// be themed without touching <see cref="SensorRenderer"/>. Consumers should start from
/// <see cref="Default"/> and override only what they need — the parameterless struct default is
/// intentionally not usable (all-zero colors). Defaults are deliberately neutral dark greys; no
/// bright status colors.
/// </summary>
public readonly record struct SensorTheme
{
    // ── Surface ───────────────────────────────────────────────────────────────
    /// <summary>Fill behind everything (the whole canvas is cleared with this).</summary>
    public PluginColor Background { get; init; }

    /// <summary>Fill of the inner tile panel. Draw the panel only when <see cref="ShowPanel"/>.</summary>
    public PluginColor Panel { get; init; }

    /// <summary>Panel border color.</summary>
    public PluginColor Border { get; init; }

    /// <summary>Gauge track (unfilled part of the bar).</summary>
    public PluginColor BarTrack { get; init; }

    /// <summary>Default gauge fill; overridden per reading by <see cref="SensorReading.Accent"/>.</summary>
    public PluginColor BarFill { get; init; }

    /// <summary>Bar thickness and corner radius (device pixels).</summary>
    public int BarHeight { get; init; }
    public int BarRadius { get; init; }

    /// <summary>Horizontal margin on each side of a gauge bar so it is not clipped by the device
    /// bezel at the left/right edges (bars are narrower than the text column).</summary>
    public int BarInsetX { get; init; }

    /// <summary>Clearance kept below the lowest content so the bottom-most gauge is not clipped by
    /// the device bezel at the bottom edge.</summary>
    public int BarBottomGap { get; init; }

    public bool ShowPanel { get; init; }
    public bool ShowBar { get; init; }

    /// <summary>Outer margin from the canvas edge to the panel (device bezel clearance).</summary>
    public int Inset { get; init; }

    /// <summary>Inner padding from the panel edge to content.</summary>
    public int Padding { get; init; }

    /// <summary>Panel corner radius / border stroke width.</summary>
    public int CornerRadius { get; init; }
    public int BorderWidth { get; init; }

    // ── Text ──────────────────────────────────────────────────────────────────
    /// <summary>Header band (single-reading tile title).</summary>
    public PluginColor HeaderColor { get; init; }
    public float HeaderFontSize { get; init; }

    public PluginColor ValueColor { get; init; }
    /// <summary>Upper bound for the main value size; the renderer shrinks it to fit the width.</summary>
    public float ValueFontSize { get; init; }

    public PluginColor UnitColor { get; init; }
    public float UnitFontSize { get; init; }

    /// <summary>Row label (left column of a multi-reading layout).</summary>
    public PluginColor CaptionColor { get; init; }
    public float CaptionFontSize { get; init; }

    /// <summary>Row value (right column of a multi-reading layout). The row font is scaled up from
    /// <see cref="CaptionFontSize"/> toward <see cref="ValueFontSize"/> as the row grows taller.</summary>
    public PluginColor RowColor { get; init; }
    public float RowFontSize { get; init; }

    /// <summary>When true, all text is stroked with <see cref="OutlineColor"/> so it stays legible
    /// over an arbitrary page wallpaper (used by the transparent theme).</summary>
    public bool OutlineText { get; init; }
    public PluginColor OutlineColor { get; init; }

    /// <summary>Neutral dark default theme.</summary>
    public static SensorTheme Default { get; } = new()
    {
        Background = new PluginColor(0x0E, 0x0E, 0x0E),
        Panel = new PluginColor(0x1A, 0x1A, 0x1A),
        Border = new PluginColor(0x30, 0x30, 0x30),
        BarTrack = new PluginColor(0x2C, 0x2C, 0x2C),
        BarFill = new PluginColor(0x6E, 0x6E, 0x6E),
        BarHeight = 8,
        BarRadius = 4,
        BarInsetX = 6,
        BarBottomGap = 4,
        ShowPanel = true,
        ShowBar = true,
        Inset = 2,
        Padding = 5,
        CornerRadius = 8,
        BorderWidth = 1,

        HeaderColor = new PluginColor(0xC8, 0xC8, 0xC8),
        HeaderFontSize = 11f,

        ValueColor = new PluginColor(0xEC, 0xEC, 0xEC),
        ValueFontSize = 30f,

        UnitColor = new PluginColor(0x9C, 0x9C, 0x9C),
        UnitFontSize = 13f,

        CaptionColor = new PluginColor(0xC8, 0xC8, 0xC8),
        CaptionFontSize = 11f,

        RowColor = new PluginColor(0xD0, 0xD0, 0xD0),
        RowFontSize = 13f,

        OutlineText = false,
        OutlineColor = PluginColor.Black
    };

    /// <summary>Transparent variant: no opaque background or panel, so the page wallpaper shows
    /// through. Text is outlined for legibility; the gauge keeps its own track for contrast.</summary>
    public static SensorTheme Transparent { get; } = Default with
    {
        Background = PluginColor.Transparent,
        ShowPanel = false,
        OutlineText = true,
        // A soft, semi-transparent halo rather than a hard black rim — the host strokes the outline
        // at a fixed 3px, which reads as a heavy black border on thin label glyphs at full opacity.
        OutlineColor = new PluginColor(0x00, 0x00, 0x00, 0x80)
    };
}
