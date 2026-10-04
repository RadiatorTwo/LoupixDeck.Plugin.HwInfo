using LoupixDeck.Plugin.HwInfo.Telemetry;

namespace LoupixDeck.Plugin.HwInfo.Rendering.Tiles;

/// <summary>One row of a component page: a metric and its label.</summary>
internal sealed record PageRow(string Metric, string Label);

/// <summary>
/// A component page (design Fig. 1, layout C): a header, one hero value with its unit and a bar,
/// then up to three rows. A page with fewer rows fills the rest with the history chart of
/// <see cref="Spark"/>. The summary page (<see cref="IsSummary"/>) is the all-at-once grid of
/// layout B instead.
/// </summary>
internal sealed record ComponentPage(
    string Id,
    string Title,
    string Hero,
    string HeroLabel,
    IReadOnlyList<PageRow> Rows,
    string? Spark = null,
    bool IsSummary = false);

/// <summary>The pages a paging tile can show, and the parameter grammar that selects them.</summary>
internal static class ComponentPages
{
    public static ComponentPage Cpu { get; } = new("cpu", "CPU", PageMetrics.CpuTemp, "TEMP",
        [new(PageMetrics.CpuClock, "CLK"), new(PageMetrics.CpuFan, "FAN"), new(PageMetrics.CpuLoad, "LOAD")]);

    public static ComponentPage Gpu { get; } = new("gpu", "GPU", PageMetrics.GpuTemp, "TEMP",
        [new(PageMetrics.GpuClock, "CLK"), new(PageMetrics.GpuFan, "FAN"), new(PageMetrics.GpuLoad, "LOAD")]);

    public static ComponentPage Ram { get; } = new("ram", "RAM", PageMetrics.RamLoad, "LOAD",
        [new(PageMetrics.RamUsed, "USED"), new(PageMetrics.RamFree, "FREE")], Spark: PageMetrics.RamLoad);

    public static ComponentPage Net { get; } = new("net", "NET", PageMetrics.NetDown, "DOWN",
        [new(PageMetrics.NetUp, "UP")], Spark: PageMetrics.NetDown);

    public static ComponentPage Disk { get; } = new("disk", "DISK", PageMetrics.DiskTemp, "TEMP",
        [new(PageMetrics.DiskRead, "READ"), new(PageMetrics.DiskWrite, "WRITE")], Spark: PageMetrics.DiskRead);

    /// <summary>All four CPU metrics at 2× (layout B). Load takes the place of
    /// the design's VCORE row, as in the Argus plugin.</summary>
    public static ComponentPage Summary { get; } = new("sum", "CPU", PageMetrics.CpuTemp, "TEMP",
        [new(PageMetrics.CpuTemp, "TEMP"), new(PageMetrics.CpuClock, "CLK"), new(PageMetrics.CpuFan, "FAN"),
            new(PageMetrics.CpuLoad, "LOAD")], IsSummary: true);

    /// <summary>CPU package power, GPU board power and both together.</summary>
    public static ComponentPage Power { get; } = new("pwr", "PWR", PageMetrics.CpuPower, "CPU",
        [new(PageMetrics.GpuPower, "GPU"), new(PageMetrics.PowerTotal, "SUM")], Spark: PageMetrics.CpuPower);

    public static ComponentPage Vram { get; } = new("vram", "VRAM", PageMetrics.VramLoad, "LOAD",
        [new(PageMetrics.VramUsed, "USED"), new(PageMetrics.VramFree, "FREE")], Spark: PageMetrics.VramLoad);

    /// <summary>The charge level, on systems with a battery.</summary>
    public static ComponentPage Battery { get; } = new("bat", "BAT", PageMetrics.BatteryLevel, "CHRG", [],
        Spark: PageMetrics.BatteryLevel);

    /// <summary>Every page, in the order <see cref="AllSelection"/> cycles them: the component pages,
    /// then the summary.</summary>
    public static IReadOnlyList<ComponentPage> All { get; } = [Cpu, Gpu, Ram, Net, Disk, Power, Vram, Battery, Summary];

    /// <summary>Selects every page in <see cref="All"/>, including pages added in later versions.
    /// The menu's "All pages" entry stores it.</summary>
    public const string AllSelection = "all";

    /// <summary>The cycle used when a tile names no pages. Separated by '|' because the host splits a
    /// command's parameters at commas. It is the list the "All pages" entry stored before
    /// <see cref="AllSelection"/> existed; kept as it was, so such tiles keep cycling exactly as before.</summary>
    public const string DefaultSelection = "cpu|gpu|ram|net|disk|sum";

    private static readonly char[] Separators = ['|', ',', ' ', ';'];

    /// <summary>
    /// Parses page lists ("cpu|gpu|sum") in order. Every entry may itself hold several ids, so a
    /// list the host split at commas ("cpu", "gpu") reads the same as one joined by '|'. Unknown
    /// ids are skipped; "all" adds every page; nothing known at all falls back to <see cref="DefaultSelection"/>.
    /// </summary>
    public static IReadOnlyList<ComponentPage> Parse(IEnumerable<string?> selections)
    {
        List<ComponentPage> pages = [];
        foreach (string? selection in selections)
        {
            foreach (string id in (selection ?? string.Empty).Split(Separators,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (id.Equals(AllSelection, StringComparison.OrdinalIgnoreCase))
                {
                    pages.AddRange(All);
                    continue;
                }

                ComponentPage? page = All.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (page is not null)
                    pages.Add(page);
            }
        }

        return pages.Count > 0 ? pages : Parse([DefaultSelection]);
    }

    /// <summary>A page is shown only while its hero metric has data (e.g. NET disappears when
    /// HWiNFO reports no network adapter).</summary>
    public static bool IsAvailable(ComponentPage page, TelemetryFrame frame) => frame.Get(page.Hero) is not null;
}
