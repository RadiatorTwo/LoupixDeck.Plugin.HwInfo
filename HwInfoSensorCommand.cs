using System.Globalization;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.HwInfo;

/// <summary>
/// Display command that renders a single HWiNFO sensor reading onto a touch
/// button. The command name is kept identical to the former built-in command.
/// </summary>
internal sealed class HwInfoSensorCommand(HwInfoService hwInfo) : IDisplayCommand
{
    public CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = "HwInfo.Sensor",
        DisplayName = "HWiNFO Sensor",
        Group = "HWiNFO",
        ParameterTemplate = "({Sensor})",
        Parameters = [new CommandParameter("Sensor", typeof(string))],
        // Surfaced per sensor through the dynamic menu.
        HiddenFromMenu = true
    };

    public ButtonTargets SupportedTargets => ButtonTargets.TouchButton;

    public TimeSpan UpdateInterval => TimeSpan.FromSeconds(2);

    public string GetText(CommandContext ctx)
    {
        if (!hwInfo.IsAvailable)
            return "N/A";

        var parameters = ctx.Parameters;
        if (parameters is not { Length: >= 1 } || string.IsNullOrWhiteSpace(parameters[0]))
            return "?";

        if (!TryParseSensorRef(parameters[0], out var sensorId, out var sensorInstance, out var readingId))
            return "?";

        var sensor = hwInfo.Sensors.FirstOrDefault(s =>
            s.SensorId == sensorId && s.SensorInstance == sensorInstance && s.ReadingId == readingId);
        if (sensor is null)
            return "?";

        var unit = string.IsNullOrEmpty(sensor.Unit) ? string.Empty : " " + sensor.Unit;
        return $"{sensor.Value:F1}{unit}";
    }

    public Task Execute(CommandContext ctx) => Task.CompletedTask;

    // Reference format: "sensorId:sensorInstance:readingId" — the triple HWiNFO keeps
    // stable across runs. Values may be decimal or 0x-prefixed hex.
    private static bool TryParseSensorRef(string raw, out uint sensorId, out uint sensorInstance, out uint readingId)
    {
        sensorId = 0;
        sensorInstance = 0;
        readingId = 0;

        var parts = raw.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
            return false;

        return TryParseUInt(parts[0], out sensorId)
               && TryParseUInt(parts[1], out sensorInstance)
               && TryParseUInt(parts[2], out readingId);
    }

    private static bool TryParseUInt(string text, out uint value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return uint.TryParse(text, out value);
    }
}
