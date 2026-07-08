using LoupixDeck.Plugin.HwInfo.Rendering;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.HwInfo;

/// <summary>
/// Display command that renders HWiNFO readings onto a touch button (90×90) via the SDK
/// image-rendering API. One command carries one sensor; a button's command sequence composes the
/// tile dynamically — the first (rendering) command reads <see cref="CommandContext.SequenceCommands"/>
/// and draws one row per sibling command (up to four). The command name and the "Sensor" parameter
/// (the stable HWiNFO triple) are unchanged, so buttons saved before the rework keep working.
/// </summary>
internal sealed class HwInfoSensorCommand(HwInfoService hwInfo) : IDisplayImageCommand
{
    public CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = "HwInfo.Sensor",
        DisplayName = "HWiNFO Sensor",
        Group = "HWiNFO",
        Icon = "\U000F0379",
        Description = "Render a live HWiNFO sensor reading on a touch button",
        ParameterTemplate = "({Sensor})",
        Parameters = [new CommandParameter("Sensor", typeof(string))],
        // Surfaced per sensor through the dynamic menu.
        HiddenFromMenu = true
    };

    public ButtonTargets SupportedTargets => ButtonTargets.TouchButton;

    public TimeSpan UpdateInterval => TimeSpan.FromSeconds(2);

    public bool RenderImage(CommandContext ctx, IRenderCanvas canvas)
    {
        bool transparent = ctx.Host.Settings.Get(HwInfoPlugin.TransparentBackgroundKey, false);

        List<SensorReading> readings = [];
        foreach (string? sensorRef in SensorReferences(ctx))
        {
            readings.Add(HwInfoReadingBuilder.Build(sensorRef, hwInfo.Sensors, hwInfo.IsAvailable));
            if (readings.Count >= SensorRenderer.MaxReadings)
                break;
        }

        SensorRenderer.Render(canvas, readings, transparent ? SensorTheme.Transparent : SensorTheme.Default);
        return true;
    }

    /// <summary>
    /// The sensor references to render, in order. On a multi-command button the whole sequence is
    /// available: take the "Sensor" parameter of every sibling that is also a HwInfo.Sensor command
    /// (non-HWiNFO commands in the sequence are ignored). A single-command button reports an empty
    /// sequence, so fall back to this command's own parameter.
    /// </summary>
    private IEnumerable<string?> SensorReferences(CommandContext ctx)
    {
        if (ctx.SequenceCommands.Count > 0)
        {
            foreach (SequenceCommand command in ctx.SequenceCommands)
            {
                if (command.Name != Descriptor.CommandName)
                    continue;

                yield return command.Parameters is { Length: >= 1 } ? command.Parameters[0] : null;
            }

            yield break;
        }

        yield return ctx.Parameters is { Length: >= 1 } ? ctx.Parameters[0] : null;
    }

    public Task Execute(CommandContext ctx) => Task.CompletedTask;
}
