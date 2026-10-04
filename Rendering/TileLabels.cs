using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using LoupixDeck.Plugin.HwInfo.Rendering.Pixel;
using LoupixDeck.Plugin.HwInfo.Rendering.Tiles;
using LoupixDeck.Plugin.HwInfo.Telemetry;

namespace LoupixDeck.Plugin.HwInfo.Rendering;

/// <summary>
/// The labels a HwInfo.Sensor tile shows, derived from the names the menu gives the sensors
/// (<see cref="SensorMenu.Name"/>), so the tile and the menu speak the same language. Each label
/// is the component ("CPU", "GPU", …) followed by the menu's entry name: the CPU's and the GPU's
/// core voltage read "CPU Core" and "GPU Core" instead of both "Core". Drives go by their letters
/// ("Disk C: Read"). The quantity is left to the unit printed beside the value.
///
/// <para>A tile header takes about eleven characters, a row label of a multi-reading tile about
/// seven, so the row label is abbreviated ("CPU C1", "GPU Hot"). Labels are unique among the
/// readings that share a unit; where the words alone would collide, a running number is added.</para>
/// </summary>
internal static partial class TileLabels
{
    public sealed record Labels(string Header, string Short);

    private sealed record Cache(IReadOnlyList<HwInfoSensor> Sensors, Dictionary<string, Labels> Labels);

    /// <summary>Width of a single-reading tile's header text (the header band minus its margins).</summary>
    private const int HeaderRoom = TileDrawing.W - 2;

    /// <summary>Characters a row label keeps beside a two-digit value at 2×; longer labels are cut
    /// when drawn, so <see cref="Shorten"/> makes the part that tells them apart fit first.</summary>
    private const int ShortBudget = 8;

    private static volatile Cache? _cache;

    private static readonly Dictionary<string, string> ComponentTags = new()
    {
        ["CPU"] = "CPU",
        ["GPU"] = "GPU",
        ["Memory"] = "RAM",
        ["Storage"] = "Disk",
        ["Network"] = "Net"
    };

    /// <summary>Words that only repeat the quantity. Dropped when something else is left.</summary>
    private static readonly Dictionary<string, string[]> QuantityWords = new()
    {
        ["Temperature"] = ["Temperature"],
        ["Power"] = ["Power"],
        ["Clock"] = ["Clock"]
    };

    /// <summary>Row-label abbreviations, applied as whole words, case-insensitively.</summary>
    private static readonly (string Word, string Short)[] Abbreviations =
    [
        ("Hot Spot", "Hot"), ("Hotspot", "Hot"), ("Memory Junction", "Mem"),
        ("Tctl/Tdie", "Tctl"), ("SVI2 TFN", "SVI2"),
        ("Infinity Fabric", "IF"), ("Memory Controller", "MC"),
        ("Remaining Life", "Life"), ("Available Spare", "Spare"),
        ("Frame Time", "FT"), ("Framerate", "FPS"),
        ("Temperature", "Temp"),
        ("Clock", "Clk"),
        ("Memory", "Mem"),
        ("Multiplier", "Mult"),
        ("Power", "Pwr"),
        ("Total", "Tot"),
        ("System", "Sys"),
        ("Average", "Avg"), ("Effective", "Eff"),
        ("Maximum", "Max"), ("Minimum", "Min"),
        ("Controller", "Ctrl"), ("Available", "Free"),
        ("Activity", "Act"), ("Space", ""),
        ("Chipset", "Chip"), ("Composite", "Comp"),
        ("Virtual", "Virt"), ("Physical", "Phys"), ("Committed", "Comm"),
        ("Package", "Pkg"), ("Utility", "Util"), ("Throttling", "Thr"),
        ("Limit", "Lim"), ("Presented", "Pres"), ("Displayed", "Disp"),
        ("read", "Rd"), ("write", "Wr"), ("down", "Dn")
    ];

    /// <summary>The labels of the reading under <paramref name="key"/> (<see cref="MetricKeys.ForSensor"/>),
    /// or null when the menu does not offer it. Computed once per sensor snapshot.</summary>
    public static Labels? For(IReadOnlyList<HwInfoSensor> sensors, string key)
    {
        Cache? cache = _cache;
        if (cache is null || !ReferenceEquals(cache.Sensors, sensors))
            _cache = cache = new Cache(sensors, Compute(sensors));

        return cache.Labels.GetValueOrDefault(key);
    }

    /// <summary>A label in full and without HWiNFO's bracketed details; the bare one is used where
    /// the full one is too long, unless it collides with another label.</summary>
    private sealed record Candidate(string Full, string Bare, bool UseBare);

    private static Dictionary<string, Labels> Compute(IReadOnlyList<HwInfoSensor> sensors)
    {
        List<SensorName> named = SensorMenu.Name(sensors);
        List<Candidate> headers = [];
        List<Candidate> shorts = [];
        foreach (SensorName name in named)
        {
            (string tag, string shortTag) = Tags(name);
            string entry = EntryWords(name);

            // "DIMM[2] (P0 CHANNEL A/DIMM 1)" → "DIMM[2]", "Total Power [% of TDP]" → "Total Power".
            string bareName = Whitespace().Replace(Brackets().Replace(name.Name, " "), " ").Trim();
            string bare = bareName.Any(char.IsLetter) && bareName != name.Name
                ? EntryWords(name with { Name = bareName })
                : entry;

            string header = Join(tag, entry);
            string compact = Join(tag, Abbreviate(entry));
            string headerFull = PixelFont.Measure(header) <= HeaderRoom ? header : compact;
            headers.Add(new Candidate(headerFull, Join(tag, Abbreviate(bare)),
                PixelFont.Measure(headerFull) > HeaderRoom && bare != entry));

            int tagWords = shortTag.Length == 0 ? 0 : shortTag.Split(' ').Length;
            string row = Join(shortTag, Aggregate().Replace(Abbreviate(entry), "$1"));
            string bareRow = Join(shortTag, Aggregate().Replace(Abbreviate(bare), "$1"));
            shorts.Add(new Candidate(Shorten(row, tagWords), Shorten(bareRow, tagWords),
                row.Length > ShortBudget && bare != entry));
        }

        List<string> headerLabels = Resolve(headers, named);
        List<string> shortLabels = Resolve(shorts, named);

        Number(headerLabels, named);
        Number(shortLabels, named);

        Dictionary<string, Labels> labels = [];
        for (int i = 0; i < named.Count; i++)
            labels[MetricKeys.ForSensor(named[i].Sensor)] = new Labels(headerLabels[i], shortLabels[i]);

        return labels;
    }

    /// <summary>The component tag of a header and of a row. Drives go by their first letter:
    /// "Disk C:" in a header, just "C:" in a row, which has no room for more.</summary>
    private static (string Header, string Short) Tags(SensorName name)
    {
        string tag = ComponentTags.GetValueOrDefault(name.Component, string.Empty);
        if (name.Component == Components.Storage && DriveLetter().Match(name.Hardware ?? "") is { Success: true } drive)
            return ($"{tag} {drive.Value}", drive.Value);

        return (tag, tag);
    }

    /// <summary>
    /// Picks each label's form: the bare one where it was asked for and no other label of the same
    /// unit reads the same, else the full one ("Clock (measured)" must not become a second "Clock").
    /// </summary>
    private static List<string> Resolve(List<Candidate> candidates, List<SensorName> named)
    {
        List<string> labels = candidates.Select(c => c.UseBare ? c.Bare : c.Full).ToList();
        foreach (IGrouping<(string, string), int> clash in Enumerable.Range(0, labels.Count)
                     .GroupBy(i => (labels[i].ToUpperInvariant(), SensorMenu.UnitTag(named[i].Sensor)))
                     .Where(g => g.Count() > 1))
        {
            foreach (int i in clash)
                labels[i] = candidates[i].Full;
        }

        return labels;
    }

    /// <summary>The entry part of the label: the menu name, minus words the tile does not need.</summary>
    private static string EntryWords(SensorName name)
    {
        HwInfoSensor sensor = name.Sensor;

        // "Read Rate" → "Read", "Current DL rate" → "Down".
        if (name.Section == "Transfer rate" && name.Component is Components.Storage or Components.Network
            && Direction(SensorMetrics.Label(sensor)) is { } direction)
            return direction;

        // "GPU Available" would not say what is available; "GPU Mem" is the memory temperature.
        if (name.Component == Components.Gpu
            && (name.Section == "Memory" || (name.Section == "Load" && name.Name == "Memory")))
            return name.Name == "Memory" ? "VRAM" : Join("VRAM", name.Name);

        // An entry that replaced its one-entry submenu is named after the quantity ("GPU > Load").
        string entry = name.MenuName == name.Section ? name.Section : name.Name;
        entry = Whitespace().Replace(entry.Replace('(', ' ').Replace(")", ""), " ").Replace(" ,", ",").Trim();

        if (QuantityWords.TryGetValue(name.Section, out string[]? words))
        {
            string stripped = entry;
            foreach (string word in words)
                stripped = ReplaceWord(stripped, word, "");

            stripped = Whitespace().Replace(stripped, " ").Trim();
            // Keep the word where nothing but a number would be left ("Temp. 4").
            if (stripped.Any(char.IsLetter))
                entry = stripped;
        }

        return entry;
    }

    /// <summary>The direction a rate's label states, as the word the row label abbreviates.</summary>
    private static string? Direction(string label)
    {
        foreach ((string word, string direction) in DirectionWords)
        {
            Regex pattern = DirectionPatterns.GetOrAdd(word,
                static w => new Regex($"(?<![A-Za-z]){w}(?![A-Za-z])", RegexOptions.IgnoreCase));
            if (pattern.IsMatch(label))
                return direction;
        }

        return null;
    }

    private static readonly (string Word, string Direction)[] DirectionWords =
    [
        ("Download", "Down"), ("DL", "Down"), ("Upload", "Up"), ("UP", "Up"), ("Read", "Read"), ("Write", "Write")
    ];

    private static string Abbreviate(string entry)
    {
        // "Core #1" → "C1"; a "Core" in front of another word adds nothing ("Core CCD1", "Core Clock").
        string text = CoreNumber().Replace(entry, "C$1");
        text = CoreBeforeWord().Replace(text, "");

        foreach ((string word, string abbreviation) in Abbreviations)
            text = ReplaceWord(text, word, abbreviation);

        return Whitespace().Replace(text, " ").Trim();
    }

    /// <summary>
    /// Cuts a row label to <see cref="ShortBudget"/> characters word by word, so every word keeps a
    /// few letters instead of the tail being lost: "Vorne Unten" → "Vor Unte", "Mem-Modul 3" →
    /// "Mem-Mo 3". Never touches the first <paramref name="tagWords"/> words (the component tag) or
    /// trailing digits.
    /// </summary>
    private static string Shorten(string label, int tagWords)
    {
        List<string> words = label.Split(' ').ToList();
        int first = tagWords;

        while (string.Join(' ', words).Length > ShortBudget)
        {
            // The longest word other than the last with more than three letters, else the last one.
            int pick = -1;
            for (int i = first; i < words.Count - 1; i++)
            {
                if (Letters(words[i]) > 3 && (pick < 0 || words[i].Length > words[pick].Length))
                    pick = i;
            }

            if (pick < 0 && words.Count - 1 >= first && Letters(words[^1]) > 3)
                pick = words.Count - 1;

            if (pick < 0)
                break;

            words[pick] = DropLetter(words[pick]);
        }

        return string.Join(' ', words);
    }

    private static int Letters(string word) => word.Count(char.IsLetter);

    /// <summary>Removes the last letter before any trailing digits ("NotConnected1" → "NotConnecte1").</summary>
    private static string DropLetter(string word)
    {
        int end = word.Length;
        while (end > 0 && !char.IsLetter(word[end - 1]))
            end--;

        return (word[..(end - 1)] + word[end..]).TrimEnd('-', '.', ',');
    }

    /// <summary>Appends a running number to labels that collide among readings of the same unit.</summary>
    private static void Number(List<string> labels, List<SensorName> named)
    {
        foreach (IGrouping<(string, string), int> clash in Enumerable.Range(0, labels.Count)
                     .GroupBy(i => (labels[i].ToUpperInvariant(), SensorMenu.UnitTag(named[i].Sensor)))
                     .Where(g => g.Count() > 1))
        {
            int number = 1;
            foreach (int i in clash)
                labels[i] = $"{labels[i]} {number++}";
        }
    }

    private static string Join(string tag, string entry)
    {
        if (tag.Length == 0 || entry.Length == 0)
            return tag.Length == 0 ? entry : tag;

        // Names sometimes start with the component already.
        if (entry.Equals(tag, StringComparison.OrdinalIgnoreCase))
            return tag;
        return entry.StartsWith(tag + " ", StringComparison.OrdinalIgnoreCase) ? entry : $"{tag} {entry}";
    }

    private static string ReplaceWord(string text, string word, string replacement) =>
        WordPatterns.GetOrAdd(word,
                static w => new Regex($@"(?<!\w){Regex.Escape(w)}(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .Replace(text, replacement);

    // The patterns built from the word lists, each parsed once. The static Regex methods keep only
    // the 15 most recently used patterns, fewer than the abbreviation list holds, so every label
    // computation parsed every pattern again.
    private static readonly ConcurrentDictionary<string, Regex> WordPatterns = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Regex> DirectionPatterns = new(StringComparer.Ordinal);

    /// <summary>"Clk max" → "max": in a row label the unit already says clock or multiplier.</summary>
    [GeneratedRegex(@"^(?:Clk|Mult)\s+(max|min|avg)$")]
    private static partial Regex Aggregate();

    [GeneratedRegex(@"\b[Cc]ore\s*#?(\d+)\b")]
    private static partial Regex CoreNumber();

    [GeneratedRegex(@"\b[Cc]ore\s+(?=\S)")]
    private static partial Regex CoreBeforeWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?<=\s)(?:\([^)]*\)|\[[^\]]*\])")]
    private static partial Regex Brackets();

    /// <summary>The first drive letter HWiNFO lists after a drive's model ("… [C:, H:]" → "C:").</summary>
    [GeneratedRegex(@"(?<=\[)[A-Z]:")]
    private static partial Regex DriveLetter();
}
