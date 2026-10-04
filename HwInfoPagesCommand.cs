using System.Collections.Concurrent;
using LoupixDeck.Plugin.HwInfo.Rendering;
using LoupixDeck.Plugin.HwInfo.Rendering.Pixel;
using LoupixDeck.Plugin.HwInfo.Rendering.Tiles;
using LoupixDeck.Plugin.HwInfo.Telemetry;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.HwInfo;

/// <summary>
/// A paging hardware tile (design Fig. 1): one component per page — CPU, GPU, RAM, NET, DISK and
/// the all-at-once summary — and a key press moves that button to the next page. The "Pages"
/// parameter lists pages separated by '|' ("cpu|gpu|sum"); several HwInfo.Pages
/// commands chained on one button add up to one cycle in sequence order, so each command can carry
/// a single page. Pages without data (e.g. NET without a network adapter) are skipped, and the
/// header's n/N counts only the pages shown. The current page is kept per button and resets on a restart.
/// </summary>
internal sealed class HwInfoPagesCommand(TelemetrySampler telemetry) : IAnimatedDisplayCommand, IDisplayImageCommand
{
    public const string CommandName = "HwInfo.Pages";

    private readonly ConcurrentDictionary<string, Cycle> _cycles = new(StringComparer.Ordinal);

    public CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = CommandName,
        DisplayName = "HWiNFO Pages",
        Group = "HWiNFO",
        Icon = "\U000F0379",
        Description = "Show hardware pages on a touch button; press it for the next page",
        ParameterTemplate = "({Pages})",
        Parameters = [new CommandParameter("Pages", typeof(string))],
        // Surfaced per page selection through the dynamic menu.
        HiddenFromMenu = true,
        // The tile fills the whole key; no host icon or caption on top of it.
        ButtonLayout = new ButtonLayoutDescriptor { Mode = ButtonLayoutMode.None }
    };

    public ButtonTargets SupportedTargets => ButtonTargets.TouchButton;

    public int TargetFps => PixelTile.TargetFps;

    public TimeSpan UpdateInterval => TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The host runs every command of a button's chain on a press, so a button with N chained
    /// HwInfo.Pages commands calls this N times per press; the page advances once per
    /// N calls. N is learned from the render call, which sees the whole chain.
    /// </summary>
    public Task Execute(CommandContext ctx)
    {
        _cycles.GetOrAdd(PositionKey(ctx), _ => new Cycle()).Press();
        return Task.CompletedTask;
    }

    public AnimationFrameInfo RenderAnimatedFrame(CommandContext ctx, IRenderCanvas canvas, AnimationFrameContext frame) =>
        PixelTile.Render(ctx, canvas, surface => Draw(ctx, surface, TileDrawing.BlinkOn(frame.Elapsed)));

    public bool RenderImage(CommandContext ctx, IRenderCanvas canvas)
    {
        PixelTile.Render(ctx, canvas, surface => Draw(ctx, surface, PixelTile.WallClockBlink()));
        return true;
    }

    private void Draw(CommandContext ctx, PixelSurface surface, bool blinkOn)
    {
        TelemetryFrame frame = telemetry.Frame;
        if (!frame.IsAvailable)
        {
            TileDrawing.Unavailable(surface, "HWINFO");
            return;
        }

        (IReadOnlyList<string?> selections, int commandsPerPress) = Selections(ctx);
        IReadOnlyList<ComponentPage> selected = ComponentPages.Parse(selections);
        List<ComponentPage> pages = selected.Where(page => ComponentPages.IsAvailable(page, frame)).ToList();
        if (pages.Count == 0)
        {
            TileDrawing.NoData(surface, selected[0].Title);
            return;
        }

        Cycle cycle = _cycles.GetOrAdd(PositionKey(ctx), _ => new Cycle());
        cycle.CommandsPerPress = commandsPerPress;
        int index = cycle.Position % pages.Count;
        string? pageIndex = pages.Count > 1 ? $"{index + 1}/{pages.Count}" : null;
        PageLayout.Draw(surface, pages[index], pageIndex, frame, blinkOn);
    }

    /// <summary>
    /// The page lists of this button in chain order, and how many HwInfo.Pages commands
    /// a press executes. A single-command button reports no sequence, so its own parameters are the list.
    /// Without a button key the position is keyed by the first command's own list, which only that
    /// command's Execute advances — so one call per press counts there.
    /// </summary>
    private static (IReadOnlyList<string?> Selections, int CommandsPerPress) Selections(CommandContext ctx)
    {
        List<SequenceCommand> chained = ctx.SequenceCommands.Where(c => c.Name == CommandName).ToList();
        if (chained.Count == 0)
            return (ctx.Parameters, 1);

        List<string?> selections = chained.SelectMany(c => c.Parameters).Select(p => (string?)p).ToList();
        bool keyed = ButtonKeys.For(ctx, string.Empty).Length > 0;
        return (selections, keyed ? chained.Count : 1);
    }

    /// <summary>The pressed/rendered button; on a host without button keys, every button whose
    /// (first) command carries the same page list shares one position.</summary>
    private static string PositionKey(CommandContext ctx) =>
        ButtonKeys.For(ctx, "pages:" + string.Join("|", ctx.Parameters));

    /// <summary>Page position of one button, advanced once per press.</summary>
    private sealed class Cycle
    {
        private readonly Lock _gate = new();
        private int _calls;

        public int Position { get; private set; }

        public int CommandsPerPress { get; set; } = 1;

        public void Press()
        {
            lock (_gate)
            {
                if (++_calls < Math.Max(1, CommandsPerPress))
                    return;

                _calls = 0;
                Position++;
            }
        }
    }
}
