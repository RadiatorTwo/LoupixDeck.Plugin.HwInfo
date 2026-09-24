using System.Text.RegularExpressions;

namespace LoupixDeck.Plugin.HwInfo;

/// <summary>
/// Sorts HWiNFO readings into the components the menu and the pages use, and into devices within
/// a component. Decided by HWiNFO's English sensor-group name (<see cref="HwInfoSensor.SensorNameOrig"/>:
/// "CPU [#0]: AMD Ryzen 7 5800X3D", "dGPU [#0]: NVIDIA GeForce RTX 4090: …", "S.M.A.R.T.: …",
/// "Network: …"), never by the displayed name, which HWiNFO translates and the user may rename.
/// </summary>
internal static partial class Components
{
    public const string Cpu = "CPU";
    public const string Gpu = "GPU";
    public const string Memory = "Memory";
    public const string Storage = "Storage";
    public const string Mainboard = "Mainboard";
    public const string Network = "Network";
    public const string Other = "Other";

    public static readonly string[] Order = [Cpu, Gpu, Memory, Storage, Mainboard, Network, Other];

    /// <summary>The Super I/O chip (sensor ids 0xF7……) is always on the mainboard; groups whose name
    /// starts with the same board name (the chipset, the VRM controllers) belong there too.</summary>
    private const uint SuperIoIdPrefix = 0xF7;

    private sealed record Cache(IReadOnlyList<HwInfoSensor> Sensors, HashSet<string> Boards,
        Dictionary<(uint, uint), string> Groups);

    private static volatile Cache? _cache;

    /// <summary>The component of <paramref name="sensor"/>, a reading of <paramref name="snapshot"/>.
    /// The mainboard's name is learned from the snapshot, so it is computed once per snapshot.</summary>
    public static string Of(HwInfoSensor sensor, IReadOnlyList<HwInfoSensor> snapshot)
    {
        Cache? cache = _cache;
        if (cache is null || !ReferenceEquals(cache.Sensors, snapshot))
            _cache = cache = new Cache(snapshot, Boards(snapshot), []);

        (uint, uint) group = (sensor.SensorId, sensor.SensorInstance);
        lock (cache.Groups)
        {
            if (!cache.Groups.TryGetValue(group, out string? component))
                cache.Groups[group] = component = Of(sensor, cache.Boards);

            return component;
        }
    }

    private static HashSet<string> Boards(IReadOnlyList<HwInfoSensor> sensors)
    {
        HashSet<string> boards = new(StringComparer.OrdinalIgnoreCase);
        foreach (HwInfoSensor sensor in sensors)
        {
            if (sensor.SensorId >> 24 == SuperIoIdPrefix && BoardName(GroupName(sensor)) is { } board)
                boards.Add(board);
        }

        return boards;
    }

    private static string Of(HwInfoSensor sensor, HashSet<string> boards)
    {
        string name = GroupName(sensor);
        if (CpuGroup().IsMatch(name))
            return Cpu;
        if (GpuGroup().IsMatch(name))
            return Gpu;
        if (name.StartsWith("S.M.A.R.T.:", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Drive:", StringComparison.OrdinalIgnoreCase))
            return Storage;
        if (name.StartsWith("Network:", StringComparison.OrdinalIgnoreCase))
            return Network;
        // "System: <board>" holds the physical and virtual memory; timings and DIMM sensors.
        if (name.StartsWith("System:", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Memory", StringComparison.OrdinalIgnoreCase)
            || name.Contains("DIMM", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("DDR", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("SPD", StringComparison.OrdinalIgnoreCase))
            return Memory;
        if (sensor.SensorId >> 24 == SuperIoIdPrefix
            || (BoardName(name) is { } board && boards.Contains(board)))
            return Mainboard;
        return Other;
    }

    /// <summary>
    /// The device a reading belongs to within its component: a CPU's groups ("…", "…: Enhanced",
    /// "…: C-State Residency") form one device, and so do a drive's S.M.A.R.T. and activity groups.
    /// Everything else is one device per sensor group.
    /// </summary>
    public static string DeviceKey(HwInfoSensor sensor, string component)
    {
        string name = GroupName(sensor);
        switch (component)
        {
            case Cpu when CpuGroup().Match(name) is { Success: true } cpu:
                return "cpu" + cpu.Groups[1].Value;
            case Memory:
                return "memory";
            case Storage:
                return "drive:" + DriveName(name);
            default:
                return $"{sensor.SensorId:X}:{sensor.SensorInstance}";
        }
    }

    /// <summary>The name of a device in the menu: the product without HWiNFO's prefixes, serial
    /// numbers and group suffixes.</summary>
    public static string DeviceName(HwInfoSensor sensor, string component)
    {
        string name = GroupName(sensor);
        switch (component)
        {
            case Cpu:
            case Gpu:
            {
                // "dGPU [#0]: NVIDIA GeForce RTX 4090: ASUS TUF …" → "NVIDIA GeForce RTX 4090".
                string rest = IndexPrefix().Replace(name, "");
                int colon = rest.IndexOf(": ", StringComparison.Ordinal);
                return colon > 0 ? rest[..colon] : rest;
            }
            case Storage:
                return Serial().Replace(DriveName(name), " ").Trim();
            case Network:
                return name["Network:".Length..].Trim();
            case Mainboard:
                // "MSI MEG X570 ACE (MS-7C35) (Nuvoton NCT6797D)" → "Nuvoton NCT6797D".
                return LastParenthesis().Match(name) is { Success: true } chip ? chip.Groups[1].Value : name;
            default:
                return name;
        }
    }

    /// <summary>The GPU group the pages show: a discrete card before an integrated one, else the
    /// first GPU.</summary>
    public static (uint Id, uint Instance)? PrimaryGpu(IReadOnlyList<HwInfoSensor> sensors)
    {
        (uint, uint)? first = null;
        foreach (HwInfoSensor sensor in sensors)
        {
            if (Of(sensor, sensors) != Gpu)
                continue;

            first ??= (sensor.SensorId, sensor.SensorInstance);
            if (!GroupName(sensor).StartsWith("iGPU", StringComparison.OrdinalIgnoreCase))
                return (sensor.SensorId, sensor.SensorInstance);
        }

        return first;
    }

    /// <summary>The group name to classify by: HWiNFO's English name, else the displayed one.</summary>
    public static string GroupName(HwInfoSensor sensor) =>
        sensor.SensorNameOrig.Length > 0 ? sensor.SensorNameOrig : sensor.SensorName;

    /// <summary>"S.M.A.R.T.: Samsung SSD 980 PRO 1TB (S5GX…) [I:]" → "Samsung SSD 980 PRO 1TB (S5GX…) [I:]".</summary>
    private static string DriveName(string name)
    {
        int colon = name.IndexOf(':');
        return colon >= 0 ? name[(colon + 1)..].Trim() : name;
    }

    /// <summary>"MSI MEG X570 ACE (MS-7C35) (Nuvoton NCT6797D)" → "MSI MEG X570 ACE (MS-7C35)".</summary>
    private static string? BoardName(string name)
    {
        Match chip = LastParenthesis().Match(name);
        return chip.Success && chip.Index > 0 ? name[..chip.Index].Trim() : null;
    }

    [GeneratedRegex(@"^CPU \[#(\d+)\]")]
    private static partial Regex CpuGroup();

    [GeneratedRegex(@"^[a-zA-Z]?GPU \[#\d+\]")]
    private static partial Regex GpuGroup();

    [GeneratedRegex(@"^[a-zA-Z]*\s*\[#\d+\]:\s*")]
    private static partial Regex IndexPrefix();

    [GeneratedRegex(@"\s*\(([^)]*)\)\s*$")]
    private static partial Regex LastParenthesis();

    /// <summary>The serial number HWiNFO puts in parentheses after a drive's model.</summary>
    [GeneratedRegex(@"\s*\([^)]*\)\s*")]
    private static partial Regex Serial();
}
