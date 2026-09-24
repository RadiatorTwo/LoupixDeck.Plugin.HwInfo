using System.Text.RegularExpressions;
using LoupixDeck.Plugin.HwInfo.Telemetry;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.HwInfo;

/// <summary>
/// Builds the <c>HwInfo.Sensor</c> part of the editor menu: readings are sorted by component (CPU,
/// GPU, Memory, Storage, Mainboard, Network, Other), then by device where a component has several
/// (drives, adapters, VRM controllers), then by quantity (Temperature, Clock, Load, …), and every
/// entry gets a name that is unique within its submenu. Names come from HWiNFO's English labels
/// (<see cref="SensorMetrics.Label"/>), like the pixel tiles, whatever language HWiNFO runs in.
///
/// <para>Only the presentation is new. Each entry still stores the HWiNFO triple
/// (<see cref="HwInfoSensorRef"/>), so saved buttons keep loading.</para>
/// </summary>
internal static partial class SensorMenu
{
    /// <summary>A quantity inside a component. <paramref name="Interleave"/> keeps readings of the
    /// same name together, so a fan's speed and its duty cycle sit next to each other.</summary>
    private sealed record Section(string Name, int Rank, bool Interleave = false);

    private static readonly string[] SectionOrder =
    [
        "Temperature", "Clock", "Load", "Memory", "Power", "Fans", "Voltage", "Current", "Multiplier",
        "Transfer rate", "Data", "Timings", "Frame rate", "Frame time", "C-States", "Status", "Other"
    ];

    /// <summary>Words that repeat the component and are dropped from the entry names
    /// ("Total CPU Usage" under CPU → "Total Usage", "Drive Temperature" under Storage).</summary>
    private static readonly Dictionary<string, string[]> ComponentWords = new()
    {
        [Components.Cpu] = ["CPU"],
        [Components.Gpu] = ["GPU"],
        [Components.Memory] = ["Memory"],
        [Components.Storage] = ["Drive"]
    };

    /// <summary>Words that repeat the quantity ("Core 0 Clock", "Read Rate", "Core 0 Ratio").</summary>
    private static readonly Dictionary<string, string[]> SectionWords = new()
    {
        ["Temperature"] = ["Temperature"],
        ["Clock"] = ["Clock"],
        ["Load"] = ["Usage", "Load"],
        ["Memory"] = ["Memory"],
        ["Power"] = ["Power"],
        ["Voltage"] = ["Voltage"],
        ["Current"] = ["Current"],
        ["Multiplier"] = ["Ratio"],
        ["Transfer rate"] = ["Rate"],
        ["C-States"] = ["Residency"]
    };

    private sealed record Entry(HwInfoSensor Sensor, int TypeRank, string BaseName);

    public static List<MenuNode> Build(IReadOnlyList<HwInfoSensor> sensors)
    {
        List<SensorName> named = Name(sensors);

        List<MenuNode> components = [];
        foreach (string component in Components.Order)
        {
            List<SensorName> ofComponent = named.Where(n => n.Component == component).ToList();
            List<IGrouping<string?, SensorName>> devices = ofComponent.GroupBy(n => n.Hardware).ToList();

            List<MenuNode> children = [];
            foreach (IGrouping<string?, SensorName> device in devices)
            {
                List<MenuNode> sections = SectionNodes(device.ToList());
                if (device.Key is null)
                    children.AddRange(sections);
                else
                    children.Add(new MenuNode { Name = device.Key, Children = sections });
            }

            if (children.Count > 0)
                components.Add(new MenuNode { Name = component, Children = children });
        }

        return components;
    }

    private static List<MenuNode> SectionNodes(List<SensorName> names)
    {
        List<IGrouping<string, SensorName>> sections = names.GroupBy(n => n.Section).ToList();

        List<MenuNode> children = [];
        foreach (IGrouping<string, SensorName> section in sections)
        {
            List<MenuNode> nodes = section.Select(n => new MenuNode
            {
                Name = n.MenuName,
                CommandName = HwInfoSensorCommand.CommandName,
                Parameters = new Dictionary<string, string> { { "Sensor", HwInfoSensorRef.Format(n.Sensor) } }
            }).ToList();

            if (sections.Count == 1 || nodes.Count == 1)
                children.AddRange(nodes);  // no extra level for a lone quantity or a lone entry
            else
                children.Add(new MenuNode { Name = section.Key, Children = nodes });
        }

        return children;
    }

    /// <summary>
    /// Names every reading the menu offers, in menu order. Tile labels are derived from these names
    /// so the menu and the tile call a reading the same thing.
    /// </summary>
    public static List<SensorName> Name(IReadOnlyList<HwInfoSensor> sensors)
    {
        List<SensorName> named = [];
        foreach (string component in Components.Order)
        {
            List<HwInfoSensor> ofComponent = sensors.Where(s => Components.Of(s, sensors) == component).ToList();

            // A device level only where the component has several devices (drives, adapters).
            List<IGrouping<string, HwInfoSensor>> devices = ofComponent
                .GroupBy(s => Components.DeviceKey(s, component))
                .ToList();
            Dictionary<string, string?> deviceNames = DeviceNames(component, devices);

            foreach (IGrouping<string, HwInfoSensor> device in devices)
                named.AddRange(DeviceEntries(component, deviceNames[device.Key], device.ToList()));
        }

        return named;
    }

    /// <summary>Display names of the devices, unique within the component; null when there is
    /// only one device.</summary>
    private static Dictionary<string, string?> DeviceNames(string component,
        List<IGrouping<string, HwInfoSensor>> devices)
    {
        Dictionary<string, string?> names = [];
        if (devices.Count == 1)
        {
            names[devices[0].Key] = null;
            return names;
        }

        Dictionary<string, int> used = new(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, HwInfoSensor> device in devices)
        {
            string name = Components.DeviceName(device.First(), component).Trim();
            if (name.Length == 0)
                name = device.Key;

            int count = used.GetValueOrDefault(name) + 1;
            used[name] = count;
            names[device.Key] = count == 1 ? name : $"{name} {count}";
        }

        return names;
    }

    private static List<SensorName> DeviceEntries(string component, string? hardware, List<HwInfoSensor> sensors)
    {
        Dictionary<Section, List<Entry>> bySection = [];
        foreach (HwInfoSensor sensor in sensors)
        {
            Section section = SectionFor(component, sensor);
            if (!bySection.TryGetValue(section, out List<Entry>? entries))
                bySection[section] = entries = [];

            entries.Add(new Entry(sensor, TypeRank(sensor), BaseName(sensor, component, section)));
        }

        List<KeyValuePair<Section, List<Entry>>> sections = bySection
            .OrderBy(pair => pair.Key.Rank)
            .ThenBy(pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<SensorName> named = [];
        foreach ((Section section, List<Entry> entries) in sections)
        {
            List<SensorName> names = SectionNames(component, hardware, section, entries);

            // A submenu with one entry is noise: the entry takes the submenu's place and name
            // (unless it is the device's only quantity, which gets no submenu either). "Other" and
            // "Status" say nothing about the reading, so a lone entry there keeps its own name.
            if (sections.Count > 1 && names.Count == 1 && section.Name is not ("Other" or "Status"))
                names[0] = names[0] with { MenuName = section.Name };

            named.AddRange(names);
        }

        return named;
    }

    /// <summary>
    /// The quantity a reading belongs to. HWiNFO files many readings as "Other"; those are sorted
    /// by their unit (a ratio is a multiplier, MB/s a transfer rate, "Yes/No" a status flag).
    /// </summary>
    private static Section SectionFor(string component, HwInfoSensor sensor)
    {
        string unit = sensor.Unit.Trim();
        string name = sensor.Type switch
        {
            HwInfoReadingType.Temperature => "Temperature",
            HwInfoReadingType.Voltage => "Voltage",
            HwInfoReadingType.Fan => "Fans",
            HwInfoReadingType.Current => "Current",
            HwInfoReadingType.Power => "Power",
            HwInfoReadingType.Clock => "Clock",
            HwInfoReadingType.Usage when Components.GroupName(sensor).Contains("C-State", StringComparison.OrdinalIgnoreCase)
                => "C-States",
            HwInfoReadingType.Usage => "Load",
            _ when unit == "x" => "Multiplier",
            // A GPU's fan duty cycle ("GPU Fan1", %) next to its speed.
            _ when unit == "%" && SensorMetrics.Label(sensor).Contains("Fan", StringComparison.OrdinalIgnoreCase) => "Fans",
            _ when (unit == "%" || IsSize(unit)) && component == Components.Memory => "Memory",
            _ when IsSize(unit) && component == Components.Gpu => "Memory",
            _ when unit == "%" => "Load",
            _ when IsSize(unit) => "Data",
            _ when MetricFormatter.ToBytesPerSecond(1, unit) is not null => "Transfer rate",
            _ when unit == "Yes/No" => "Status",
            _ when unit == "T" => "Timings",
            _ when unit == "FPS" => "Frame rate",
            _ when unit == "ms" => "Frame time",
            _ => "Other"
        };

        int rank = Array.IndexOf(SectionOrder, name);
        return new Section(name, rank >= 0 ? rank : SectionOrder.Length, Interleave: name == "Fans");
    }

    private static bool IsSize(string unit) => unit is "KB" or "MB" or "GB" or "TB";

    /// <summary>Order of readings of one name inside a quantity: speed before duty cycle.</summary>
    private static int TypeRank(HwInfoSensor sensor) => sensor.Unit == "%" ? 1 : 0;

    private static List<SensorName> SectionNames(string component, string? hardware, Section section,
        List<Entry> entries)
    {
        List<Entry> ordered = section.Interleave
            ? entries.GroupBy(e => e.BaseName, StringComparer.OrdinalIgnoreCase)
                .SelectMany(g => g.OrderBy(e => e.TypeRank))
                .ToList()
            : entries;

        // Name the unit only where it tells entries apart: "Fan1 (RPM)" beside "Fan1 (%)",
        // but plain "Core 0" among clock-only readings.
        bool showUnit = ordered.Select(e => UnitTag(e.Sensor)).Where(u => u.Length > 0).Distinct().Count() > 1;

        List<string> tags = ordered.Select(e => showUnit ? UnitTag(e.Sensor) : "").ToList();
        List<string> baseNames = ordered.Select(e => e.BaseName).ToList();

        // Readings named identically ("Output Power" twice) get a running number.
        foreach (IGrouping<string, int> clash in Enumerable.Range(0, ordered.Count)
                     .GroupBy(i => Compose(baseNames[i], tags[i]).ToUpperInvariant())
                     .Where(g => g.Count() > 1))
        {
            int number = 1;
            foreach (int i in clash)
                baseNames[i] = $"{ordered[i].BaseName} {number++}";
        }

        List<string> names = Enumerable.Range(0, ordered.Count).Select(i => Compose(baseNames[i], tags[i])).ToList();

        // Last resort for anything still ambiguous: the reading id.
        foreach (IGrouping<string, int> clash in Enumerable.Range(0, ordered.Count)
                     .GroupBy(i => names[i], StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            foreach (int i in clash)
            {
                uint index = ordered[i].Sensor.ReadingId & 0xFFFF;
                names[i] = $"{names[i]} #{index}";
                baseNames[i] = $"{baseNames[i]} #{index}";
            }
        }

        return Enumerable.Range(0, ordered.Count)
            .Select(i => new SensorName(ordered[i].Sensor, component, hardware, section.Name, baseNames[i], names[i]))
            .ToList();
    }

    private static string Compose(string name, string unitTag) =>
        unitTag.Length == 0 ? name : $"{name} ({unitTag})";

    /// <summary>
    /// The entry name before units and numbering: HWiNFO's label without the words that repeat the
    /// component or the quantity, or the quantity's name where nothing else is left.
    /// </summary>
    private static string BaseName(HwInfoSensor sensor, string component, Section section)
    {
        string label = SensorMetrics.Label(sensor).Trim();
        if (label.Length == 0)
            label = $"#{sensor.ReadingId & 0xFFFF}";

        // "Core 0 Clock (perf #1/1)": the ranking changes with every boot.
        label = PerfRank().Replace(label, "");

        if (ComponentWords.TryGetValue(component, out string[]? words))
        {
            foreach (string word in words)
                label = DropWord(label, word);
        }

        if (SectionWords.TryGetValue(section.Name, out string[]? sectionWords))
        {
            foreach (string word in sectionWords)
                label = DropWord(label, word);
        }

        return label.Length == 0 ? section.Name : label;
    }

    /// <summary>
    /// Removes <paramref name="word"/> (and a "/" joined to it, as in "CPU/Thread") outside
    /// parentheses, where something other than a number or a parenthesis is left ("Temperature 2"
    /// and "Clock (measured)" stay, "Thermal Throttling (PROCHOT CPU)" keeps its CPU).
    /// </summary>
    private static string DropWord(string label, string word)
    {
        Regex pattern = new($@"(?<![\w/]){Regex.Escape(word)}(?![\w-])/?", RegexOptions.IgnoreCase);
        // Split keeps the parenthesised parts (the captured separators) at the odd positions.
        string[] parts = Parentheses().Split(label);
        for (int i = 0; i < parts.Length; i += 2)
            parts[i] = pattern.Replace(parts[i], "");

        string stripped = string.Concat(parts);
        stripped = Whitespace().Replace(stripped, " ").Replace("( ", "(").Trim();
        return Parenthesis().Replace(stripped, "").Any(char.IsLetter) ? stripped : label;
    }

    /// <summary>The unit that tells readings of one quantity apart.</summary>
    public static string UnitTag(HwInfoSensor sensor) => sensor.Unit.Trim();

    [GeneratedRegex(@"\s*\(perf #[^)]*\)")]
    private static partial Regex PerfRank();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parenthesis();

    [GeneratedRegex(@"(\([^)]*\))")]
    private static partial Regex Parentheses();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>
/// How the menu names a reading. <paramref name="Hardware"/> is the device level (null where the
/// component has one device); <paramref name="Name"/> is the entry name without the unit that sets
/// it apart from its neighbours ("Fan1"); <paramref name="MenuName"/> is what the menu shows
/// ("Fan1 (RPM)", or the quantity's name when the entry replaces a one-entry submenu).
/// </summary>
internal sealed record SensorName(HwInfoSensor Sensor, string Component, string? Hardware, string Section,
    string Name, string MenuName);
