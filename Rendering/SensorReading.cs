using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.HwInfo.Rendering;

/// <summary>
/// One sensor reading to draw as a row (or, when it is the only one, as a full tile). Decoupled
/// from HWiNFO so <see cref="SensorRenderer"/> stays a pure, reusable drawing component. All numbers
/// arrive pre-formatted as strings — unit scaling (e.g. MHz→GHz) and decimal formatting happen in the
/// caller (see <see cref="HwInfoReadingBuilder"/>).
/// </summary>
/// <param name="Header">Full label / tile title (e.g. "CPU Package", "Core 7"). Used when the
/// reading fills the tile on its own.</param>
/// <param name="Value">Main value string (e.g. "36", "11.3").</param>
/// <param name="Unit">Unit drawn small next to the value (e.g. "°C", "%", "GHz"). May be empty.</param>
/// <param name="Fraction">Gauge fill 0..1 for the value, or null when the reading has no meaningful
/// scale (no bar is drawn).</param>
/// <param name="Accent">Accent color for the gauge fill, or null for the theme's neutral default.</param>
/// <param name="ShortHeader">Compact label used when the reading shares the tile as one of several
/// rows (e.g. "Core 7" for a "CPU Core 7" header), so it fits beside the value. Null → use
/// <paramref name="Header"/>.</param>
public sealed record SensorReading(
    string Header,
    string Value,
    string Unit,
    double? Fraction = null,
    PluginColor? Accent = null,
    string? ShortHeader = null);
