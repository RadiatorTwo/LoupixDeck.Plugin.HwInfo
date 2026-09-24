using LoupixDeck.Plugin.HwInfo.Rendering.Pixel;
using LoupixDeck.Plugin.HwInfo.Rendering.Tiles;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.HwInfo.Rendering;

/// <summary>
/// The render path shared by the HWiNFO display commands: draw into a fresh
/// <see cref="PixelSurface"/>, send it to the host canvas and report the frame's content hash, so
/// the host pushes to the device only when a pixel actually changed.
/// </summary>
internal static class PixelTile
{
    /// <summary>
    /// Redraw rate. Values change once a second and the critical blink runs at 1 Hz; 4 fps keeps a
    /// page switch after a key press under 250 ms. Unchanged frames cost no device I/O.
    /// </summary>
    public const int TargetFps = 4;

    public static AnimationFrameInfo Render(CommandContext ctx, IRenderCanvas canvas, Action<PixelSurface> draw)
    {
        bool transparent = ctx.Host.Settings.Get(HwInfoPlugin.TransparentBackgroundKey, false);
        PixelSurface surface = new(canvas.Width, canvas.Height,
            transparent ? PixelPalette.Transparent : PixelPalette.Default);

        draw(surface);
        PixelPresenter.Present(surface, canvas);
        return AnimationFrameInfo.Frame(surface.ContentHash());
    }

    /// <summary>The blink phase for the poll-driven path, which has no frame clock.</summary>
    public static bool WallClockBlink() => TileDrawing.BlinkOn(TimeSpan.FromMilliseconds(Environment.TickCount64));
}
