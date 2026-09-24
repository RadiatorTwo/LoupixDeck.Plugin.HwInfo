using System.Runtime.CompilerServices;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.HwInfo.Rendering.Pixel;

/// <summary>
/// Copies a <see cref="PixelSurface"/> onto the host canvas. From SDK 1.26.0 the host blits the
/// framebuffer in one call (<c>IRenderCanvas.DrawPixels</c>, ~4 µs under the host's render lock).
/// An older host has no such member, so the frame is sent as rectangles there: horizontal runs of
/// one color, merged downwards while the run below is identical. A rectangle on whole-pixel
/// coordinates covers each pixel fully, so the host's anti-aliased fill still lands exactly
/// (measured: 0 differing pixels, ~300 rectangles and ~0.2 ms for a full page). Both paths leave
/// transparent pixels alone, which is what the transparent mode relies on.
/// </summary>
internal static class PixelPresenter
{
    private static bool _hostBlits = SdkInfo.Version >= new Version(1, 26, 0);

    public static void Present(PixelSurface surface, IRenderCanvas canvas)
    {
        if (_hostBlits)
        {
            try
            {
                Blit(surface, canvas);
                return;
            }
            catch (MissingMethodException)
            {
                // A pre-release 1.26.0 host built before the member existed.
                _hostBlits = false;
            }
        }

        PresentAsRectangles(surface, canvas);
    }

    // Kept out of line: the JIT resolves the member when it compiles this method, which must not
    // happen on a host whose SDK lacks it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Blit(PixelSurface surface, IRenderCanvas canvas) =>
        canvas.DrawPixels(surface.Pixels, surface.Width, surface.Height);

    private static void PresentAsRectangles(PixelSurface surface, IRenderCanvas canvas)
    {
        uint background = surface.Palette.Background;
        if ((background >> 24) != 0)
            canvas.Clear(PixelPalette.ToPluginColor(background));

        ReadOnlySpan<uint> pixels = surface.Pixels;
        int width = surface.Width;

        // Rectangles still open for extension, keyed by (x, width, color) of their bottom run.
        Dictionary<(int X, int Width, uint Color), Rect> open = [];
        Dictionary<(int X, int Width, uint Color), Rect> next = [];

        for (int y = 0; y < surface.Height; y++)
        {
            ReadOnlySpan<uint> row = pixels.Slice(y * width, width);
            int x = 0;
            while (x < width)
            {
                uint color = row[x];
                int start = x;
                while (x < width && row[x] == color)
                    x++;

                if (color == background || (color >> 24) == 0)
                    continue;

                (int, int, uint) key = (start, x - start, color);
                next[key] = open.Remove(key, out Rect above)
                    ? above with { Height = above.Height + 1 }
                    : new Rect(start, y, x - start, 1, color);
            }

            // Whatever was not continued on this row is complete.
            foreach (Rect rect in open.Values)
                Draw(canvas, rect);

            (open, next) = (next, open);
            next.Clear();
        }

        foreach (Rect rect in open.Values)
            Draw(canvas, rect);
    }

    private static void Draw(IRenderCanvas canvas, Rect rect) =>
        canvas.FillRectangle(rect.X, rect.Y, rect.Width, rect.Height, PixelPalette.ToPluginColor(rect.Color));

    private readonly record struct Rect(int X, int Y, int Width, int Height, uint Color);
}
