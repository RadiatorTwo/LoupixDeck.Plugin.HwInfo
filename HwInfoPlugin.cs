using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.HwInfo;

/// <summary>
/// Entry point of the HWiNFO plugin (Windows only). Reads HWiNFO's shared-memory
/// sensor interface and exposes one display command plus a live sensor menu.
/// </summary>
public sealed class HwInfoPlugin : LoupixPlugin, IMenuContributor, IPluginSettingsPage
{
    /// <summary>Settings key: when true, buttons are drawn without an opaque background so the page
    /// wallpaper shows through. Read by the display command at render time.</summary>
    public const string TransparentBackgroundKey = "background.transparent";

    private readonly HwInfoService _service = new();
    private List<IPluginCommand> _commands = [];
    private IPluginHost? _host;

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "hwinfo",
        Name = "HWiNFO",
        Version = new Version(1, 0, 0),
        SdkVersion = new Version(1, 16, 0),
        Author = "RadiatorTwo",
        Description = "Display HWiNFO sensor readings on touch buttons; chain several to compose a multi-sensor tile."
    };

    public override void Initialize(IPluginHost host)
    {
        _host = host;
        _commands = [new HwInfoSensorCommand(_service)];
        _service.Start();
    }

    public override void Shutdown() => _service.Stop();

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
            // HWiNFO's natural grouping is the parent sensor (CPU, GPU, a drive, …).
            foreach (var sensorGroup in sensors
                         .GroupBy(s => new { s.SensorId, s.SensorInstance, s.SensorName })
                         .OrderBy(g => g.Key.SensorName))
            {
                var name = string.IsNullOrWhiteSpace(sensorGroup.Key.SensorName)
                    ? $"Sensor 0x{sensorGroup.Key.SensorId:X}"
                    : sensorGroup.Key.SensorName;

                var readings = new List<MenuNode>();
                foreach (var sensor in sensorGroup.OrderBy(s => s.ReadingId))
                {
                    var label = string.IsNullOrWhiteSpace(sensor.Label)
                        ? $"#{sensor.ReadingId}"
                        : sensor.Label;

                    readings.Add(new MenuNode
                    {
                        Name = label,
                        CommandName = "HwInfo.Sensor",
                        Parameters = new Dictionary<string, string>
                        {
                            { "Sensor", $"{sensor.SensorId}:{sensor.SensorInstance}:{sensor.ReadingId}" }
                        }
                    });
                }

                groupChildren.Add(new MenuNode { Name = name, Children = readings });
            }
        }

        IReadOnlyList<MenuNode> result = [new MenuNode { Name = "HWiNFO", Children = groupChildren }];
        return Task.FromResult(result);
    }

    // ───────── IPluginSettingsPage — transparency + status ─────────

    public IReadOnlyList<PluginSettingDescriptor> SettingsSchema { get; } =
    [
        new PluginSettingDescriptor
        {
            Key = TransparentBackgroundKey,
            Label = "Transparent background",
            Kind = PluginSettingKind.Toggle,
            DefaultValue = false,
            Description = "Draw buttons without an opaque background so the page wallpaper shows through. " +
                          "Text is outlined for legibility."
        }
    ];

    public IReadOnlyList<PluginSettingAction> SettingsActions => _settingsActions ??=
    [
        new PluginSettingAction
        {
            Label = "Show Status",
            Invoke = () => Task.FromResult(_service.Diagnostics)
        }
    ];

    private IReadOnlyList<PluginSettingAction>? _settingsActions;

    public void OnSettingsSaved()
    {
        // Repaint bound touch buttons immediately so a transparency toggle is visible at once
        // (otherwise it would only apply on the command's next poll).
        _host?.RequestButtonRefresh("HwInfo.Sensor");
    }
}
