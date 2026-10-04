using System.Runtime.CompilerServices;
using LoupixDeck.Plugin.HwInfo.Rendering.Tiles;
using LoupixDeck.Plugin.HwInfo.Telemetry;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.HwInfo;

/// <summary>
/// Entry point of the HWiNFO plugin (Windows only). Reads HWiNFO's shared-memory sensor
/// interface, samples it once a second into histories and alert states, and exposes two pixel-tile
/// display commands through a live menu: <c>HwInfo.Sensor</c> (one reading per command; chain
/// several for a multi-row tile) and <c>HwInfo.Pages</c> (component pages, a key press shows the
/// next one) — the same tiles as the Argus Monitor plugin.
/// </summary>
public sealed class HwInfoPlugin : LoupixPlugin, IMenuContributor, IPluginSettingsPage, IPluginRequirements
{
    /// <summary>Settings key: when true, buttons are drawn without an opaque background so the page
    /// wallpaper shows through. Read by the display command at render time.</summary>
    public const string TransparentBackgroundKey = "background.transparent";

    /// <summary>Settings key: the CPU's maximum junction temperature in °C. CPU warn/critical
    /// limits are TjMax − 15 / TjMax − 5.</summary>
    public const string CpuTjMaxKey = "thresholds.cpuTjMax";

    private const long DefaultTjMax = 100;

    private readonly HwInfoService _service = new();
    private TelemetrySampler? _telemetry;
    private List<IPluginCommand> _commands = [];
    private IPluginHost? _host;

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "hwinfo",
        Name = "HWiNFO",
        Version = new Version(1, 1, 0),
        SdkVersion = new Version(1, 28, 0),
        Author = "RadiatorTwo",
        Description = "Display HWiNFO sensor readings on touch buttons; chain several to compose a multi-sensor tile.",
        Icon = LoadIcon()
    };

    /// <summary>The plugin icon (icon.png, embedded). Missing data only costs the icon.</summary>
    private static byte[]? LoadIcon()
    {
        using Stream? stream = typeof(HwInfoPlugin).Assembly.GetManifestResourceStream("LoupixDeck.Plugin.HwInfo.icon.png");
        if (stream == null) return null;

        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public override void Initialize(IPluginHost host)
    {
        _host = host;
        _telemetry = new TelemetrySampler(_service, ReadTjMax);
        _commands = [new HwInfoSensorCommand(_telemetry), new HwInfoPagesCommand(_telemetry)];
        _service.Start();
        _telemetry.Start();
    }

    public override void Shutdown()
    {
        _telemetry?.Stop();
        _service.Stop();
    }

    // ───────── IPluginRequirements ─────────

    /// <summary>
    /// One requirement: HWiNFO running with Shared Memory Support on. The host asks right after
    /// loading, possibly before the poll loop has connected, so an unconnected service probes the
    /// shared memory directly instead of reporting a false "not met". Texts are English keys the
    /// host translates through the plugin's strings files.
    /// </summary>
    public IReadOnlyList<PluginRequirement> GetRequirements()
    {
        string? problem = _service.IsAvailable ? null : HwInfoService.ProbeSharedMemory();
        return
        [
            new PluginRequirement
            {
                Id = "hwinfo-shared-memory",
                Name = "HWiNFO shared memory",
                IsMet = problem is null,
                Message = problem,
                InstallHint = "Run HWiNFO and turn on 'Shared Memory Support' in its settings."
            }
        ];
    }

    private double ReadTjMax()
    {
        long tjMax = _host?.Settings.Get(CpuTjMaxKey, DefaultTjMax) ?? DefaultTjMax;
        return Math.Clamp(tjMax, 60, 125);
    }

    public override IEnumerable<IPluginCommand> GetCommands() => _commands;

    public override IReadOnlyList<CommandGroupDescriptor> GetCommandGroups() =>
    [
        new CommandGroupDescriptor { Group = "HWiNFO", Description = "System sensors and monitoring", Icon = "\U000F0379", Section = CommandGroupSection.Plugins }
    ];

    // ───────── IMenuContributor — dynamic sensor tree ─────────

    public Task<IReadOnlyList<MenuNode>> GetMenuNodes(ButtonTargets target)
    {
        // Sensor readings are touch-button display content only.
        if (target != ButtonTargets.TouchButton)
            return Task.FromResult<IReadOnlyList<MenuNode>>([]);

        var groupChildren = new List<MenuNode>();
        var sensors = _service.Sensors;

        if (!_service.IsAvailable || sensors.Count == 0)
        {
            groupChildren.Add(new MenuNode { Name = "HWiNFO not available" });
        }
        else
        {
            groupChildren.Add(new MenuNode { Name = "Pages", Children = PageNodes() });

            // One entry per reading (one command each), sorted by component, device and quantity.
            // Combine several on a button via its command sequence to get a multi-row tile.
            groupChildren.AddRange(SensorMenu.Build(sensors));
        }

        IReadOnlyList<MenuNode> result = [new MenuNode { Name = "HWiNFO", Children = groupChildren }];
        return Task.FromResult(result);
    }

    /// <summary>The paging tile (every page, press for the next) and one fixed tile per page.</summary>
    private static List<MenuNode> PageNodes() =>
    [
        PagesNode("All pages (press to cycle)", ComponentPages.DefaultSelection),
        PagesNode("CPU page", ComponentPages.Cpu.Id),
        PagesNode("GPU page", ComponentPages.Gpu.Id),
        PagesNode("RAM page", ComponentPages.Ram.Id),
        PagesNode("Network page", ComponentPages.Net.Id),
        PagesNode("Disk page", ComponentPages.Disk.Id),
        PagesNode("CPU summary", ComponentPages.Summary.Id),
        PagesNode("Power page", ComponentPages.Power.Id),
        PagesNode("VRAM page", ComponentPages.Vram.Id),
        PagesNode("Battery page", ComponentPages.Battery.Id)
    ];

    private static MenuNode PagesNode(string name, string pages) => new()
    {
        Name = name,
        CommandName = HwInfoPagesCommand.CommandName,
        Parameters = new Dictionary<string, string> { { "Pages", pages } }
    };

    // ───────── IPluginSettingsPage — transparency, TjMax + status ─────────

    public IReadOnlyList<PluginSettingDescriptor> SettingsSchema { get; } =
    [
        new PluginSettingDescriptor
        {
            Key = TransparentBackgroundKey,
            Label = "Transparent background",
            Kind = PluginSettingKind.Toggle,
            DefaultValue = false,
            Description = "Draw buttons without an opaque background so the page wallpaper shows through. " +
                          "Text gets a 1-pixel shadow for legibility."
        },
        new PluginSettingDescriptor
        {
            Key = CpuTjMaxKey,
            Label = "CPU TjMax (°C)",
            Kind = PluginSettingKind.Number,
            DefaultValue = DefaultTjMax,
            Description = "Maximum junction temperature of your CPU, from the vendor's spec sheet " +
                          "(typically 95 for AMD Ryzen, 100–105 for Intel). CPU temperature turns amber " +
                          "at TjMax − 15 and red at TjMax − 5."
        }
    ];

    public IReadOnlyList<PluginSettingAction> SettingsActions => _settingsActions ??=
    [
        new PluginSettingAction
        {
            Label = "Show Status",
            Invoke = () =>
            {
                string text = Tr(_service.Status);
                if (_service.LastError is { } error)
                    text += "\n" + string.Format(Tr("Last error: {0}"), Tr(error));
                return Task.FromResult(text);
            }
        }
    ];

    private IReadOnlyList<PluginSettingAction>? _settingsActions;

    /// <summary>Translates runtime text through the plugin's strings files; hosts before SDK 1.24
    /// have no <see cref="IPluginHost.Tr"/> and get the English text.</summary>
    private string Tr(string english)
    {
        try
        {
            return _host is null ? english : HostTr(_host, english);
        }
        catch (MissingMethodException)
        {
            return english;
        }
    }

    private string Tr(HwInfoDiagnostics diagnostics) => string.Format(Tr(diagnostics.Format), diagnostics.Args);

    // Kept out of line: the JIT resolves IPluginHost.Tr when it compiles this method, which throws
    // on a host without it — inside Tr's try block rather than in its caller.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string HostTr(IPluginHost host, string english) => host.Tr(english);

    public void OnSettingsSaved()
    {
        // Tiles redraw several times a second and pick up the new settings on their own; this
        // only covers a host that drives them through the slower poll path.
        _host?.RequestButtonRefresh(HwInfoSensorCommand.CommandName);
        _host?.RequestButtonRefresh(HwInfoPagesCommand.CommandName);
    }
}
