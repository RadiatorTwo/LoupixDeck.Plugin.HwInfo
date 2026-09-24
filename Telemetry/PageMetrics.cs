using System.Text.RegularExpressions;

namespace LoupixDeck.Plugin.HwInfo.Telemetry;

/// <summary>
/// The derived metrics the component pages show (CPU temperature, GPU clock, RAM free …). Each is
/// picked out of the HWiNFO snapshot by component (<see cref="Components"/>), reading type and
/// HWiNFO's English label (<see cref="HwInfoSensor.LabelOrig"/>): unlike the displayed label it is
/// the same on every system, whatever language HWiNFO runs in. Position is only the fallback.
/// </summary>
internal static partial class PageMetrics
{
    public const string CpuTemp = "cpu.temp";
    public const string CpuClock = "cpu.clock";
    public const string CpuFan = "cpu.fan";
    public const string CpuLoad = "cpu.load";
    public const string CpuPower = "cpu.power";
    public const string GpuTemp = "gpu.temp";
    public const string GpuClock = "gpu.clock";
    public const string GpuFan = "gpu.fan";
    public const string GpuLoad = "gpu.load";
    public const string RamLoad = "ram.load";
    public const string RamUsed = "ram.used";
    public const string RamFree = "ram.free";
    public const string NetDown = "net.down";
    public const string NetUp = "net.up";
    public const string DiskTemp = "disk.temp";
    public const string DiskRead = "disk.read";
    public const string DiskWrite = "disk.write";

    /// <summary>One derived metric: how to read it from a snapshot and how to describe it.</summary>
    public sealed record Definition(
        string Id,
        Func<IReadOnlyList<HwInfoSensor>, double?> Read,
        Func<double, MetricInfo> Describe);

    private static readonly string[] Download = ["Download", "DL"];
    private static readonly string[] Upload = ["Upload", "UP"];
    private static readonly string[] Read = ["Read"];
    private static readonly string[] Write = ["Write"];

    public static IReadOnlyList<Definition> All { get; } =
    [
        // Tctl/Tdie on AMD, the package on Intel; else the hottest real reading.
        new(CpuTemp,
            s => Named(CpuTemperatures(s), "CPU (Tctl/Tdie)") ?? Named(CpuTemperatures(s), "CPU Package")
                 ?? Max(CpuTemperatures(s)),
            tj => new MetricInfo(MetricFormat.Temperature, 30, tj, ThresholdKind.CpuTemperature)),
        // The fastest core ("Core 3 Clock (perf #2/4)"); effective clocks and the bus are no core clocks.
        new(CpuClock,
            s => Max(Of(s, Components.Cpu, HwInfoReadingType.Clock).Where(x => CoreClock().IsMatch(Label(x))))
                 ?? Named(Of(s, Components.Cpu, HwInfoReadingType.Clock), "Average Effective Clock"),
            _ => new MetricInfo(MetricFormat.ClockMhz, 800, 5800, Smooth: true, GrowToPeak: true)),
        new(CpuFan,
            s => CpuFanSpeed(s),
            _ => new MetricInfo(MetricFormat.Rpm, 0, 2500, ThresholdKind.CpuFanStall, GrowToPeak: true)),
        new(CpuLoad,
            s => Named(Of(s, Components.Cpu, HwInfoReadingType.Usage), "Total CPU Usage"),
            _ => new MetricInfo(MetricFormat.Percent, 0, 100, Smooth: true)),
        new(CpuPower,
            s => Named(Of(s, Components.Cpu, HwInfoReadingType.Power), "CPU Package Power")
                 ?? Named(Of(s, Components.Cpu, HwInfoReadingType.Power), "CPU PPT"),
            _ => new MetricInfo(MetricFormat.Watt, 0, 100, GrowToPeak: true)),

        new(GpuTemp,
            s => Named(OfGpu(s, HwInfoReadingType.Temperature), "GPU Temperature")
                 ?? First(OfGpu(s, HwInfoReadingType.Temperature).Where(x => !SensorMetrics.IsTemperatureLimit(x))),
            _ => new MetricInfo(MetricFormat.Temperature, 30, 95, ThresholdKind.GpuTemperature)),
        new(GpuClock,
            s => Named(OfGpu(s, HwInfoReadingType.Clock), "GPU Clock") ?? First(OfGpu(s, HwInfoReadingType.Clock)),
            _ => new MetricInfo(MetricFormat.ClockMhz, 200, 3200, Smooth: true, GrowToPeak: true)),
        new(GpuFan,
            s => First(OfGpu(s, HwInfoReadingType.Fan)),
            _ => new MetricInfo(MetricFormat.Rpm, 0, 3300, ThresholdKind.GpuFanStall, GrowToPeak: true)),
        new(GpuLoad,
            s => Named(OfGpu(s, HwInfoReadingType.Usage), "GPU Core Load")
                 ?? Named(OfGpu(s, HwInfoReadingType.Usage), "GPU Utilization")
                 ?? First(OfGpu(s, HwInfoReadingType.Usage)),
            _ => new MetricInfo(MetricFormat.Percent, 0, 100, Smooth: true)),

        new(RamLoad,
            s => Named(Of(s, Components.Memory, HwInfoReadingType.Other), "Physical Memory Load"),
            _ => new MetricInfo(MetricFormat.Percent, 0, 100, ThresholdKind.RamLoad)),
        new(RamUsed,
            s => Named(Of(s, Components.Memory, HwInfoReadingType.Other), "Physical Memory Used"),
            _ => new MetricInfo(MetricFormat.Megabytes, 0, 0)),
        new(RamFree,
            s => Named(Of(s, Components.Memory, HwInfoReadingType.Other), "Physical Memory Available"),
            _ => new MetricInfo(MetricFormat.Megabytes, 0, 0)),

        new(NetDown,
            s => Directed(MainAdapter(s), Download, Upload, 0),
            _ => new MetricInfo(MetricFormat.BytesPerSecond, 0, 0)),
        new(NetUp,
            s => Directed(MainAdapter(s), Upload, Download, 1),
            _ => new MetricInfo(MetricFormat.BytesPerSecond, 0, 0)),

        // All drives: the hottest one, and the rates summed.
        new(DiskTemp,
            s => Max(Of(s, Components.Storage, HwInfoReadingType.Temperature)
                .Where(x => !SensorMetrics.IsTemperatureLimit(x))),
            _ => new MetricInfo(MetricFormat.Temperature, 20, 80, ThresholdKind.StorageTemperature)),
        new(DiskRead,
            s => DriveRates(s, Read, Write, 0),
            _ => new MetricInfo(MetricFormat.BytesPerSecond, 0, 0)),
        new(DiskWrite,
            s => DriveRates(s, Write, Read, 1),
            _ => new MetricInfo(MetricFormat.BytesPerSecond, 0, 0))
    ];

    private static string Label(HwInfoSensor sensor) => SensorMetrics.Label(sensor);

    private static List<HwInfoSensor> Of(IReadOnlyList<HwInfoSensor> sensors, string component, HwInfoReadingType type) =>
        sensors.Where(s => s.Type == type && Components.Of(s, sensors) == component).ToList();

    private static List<HwInfoSensor> CpuTemperatures(IReadOnlyList<HwInfoSensor> sensors) =>
        Of(sensors, Components.Cpu, HwInfoReadingType.Temperature)
            .Where(s => !SensorMetrics.IsTemperatureLimit(s))
            .ToList();

    private static List<HwInfoSensor> OfGpu(IReadOnlyList<HwInfoSensor> sensors, HwInfoReadingType type)
    {
        if (Components.PrimaryGpu(sensors) is not { } gpu)
            return [];

        return sensors.Where(s => s.SensorId == gpu.Id && s.SensorInstance == gpu.Instance && s.Type == type).ToList();
    }

    /// <summary>A fan named for the CPU, else the pump of a water cooler, else the first mainboard fan.</summary>
    private static double? CpuFanSpeed(IReadOnlyList<HwInfoSensor> sensors)
    {
        List<HwInfoSensor> fans = sensors
            .Where(s => s.Type == HwInfoReadingType.Fan && Components.Of(s, sensors) != Components.Gpu)
            .ToList();
        return First(fans.Where(s => HasWord(Label(s), "CPU")))
               ?? First(fans.Where(s => HasWord(Label(s), "Pump")))
               ?? First(fans.Where(s => Components.Of(s, sensors) == Components.Mainboard));
    }

    /// <summary>
    /// The rate readings of the adapter that carried the most data: HWiNFO lists every adapter,
    /// and their order says nothing about which one is in use.
    /// </summary>
    private static List<HwInfoSensor> MainAdapter(IReadOnlyList<HwInfoSensor> sensors)
    {
        List<HwInfoSensor> network = sensors.Where(s => Components.Of(s, sensors) == Components.Network).ToList();

        (uint, uint)? adapter = network
            .GroupBy(s => (s.SensorId, s.SensorInstance))
            .Where(g => g.Any(IsRate))
            .OrderByDescending(g => g.Where(s => !IsRate(s) && s.Unit is "MB" or "GB" or "KB" or "TB")
                .Sum(s => Value(s) ?? 0))
            .Select(g => ((uint, uint)?)g.Key)
            .FirstOrDefault();

        return network.Where(s => (s.SensorId, s.SensorInstance) == adapter && IsRate(s)).ToList();
    }

    private static bool IsRate(HwInfoSensor sensor) => MetricFormatter.ToBytesPerSecond(1, sensor.Unit) is not null;

    /// <summary>
    /// The first rate among <paramref name="rates"/> whose label says <paramref name="direction"/>;
    /// the one at <paramref name="fallbackOrdinal"/> when no rate names either direction.
    /// </summary>
    private static double? Directed(List<HwInfoSensor> rates, string[] direction, string[] opposite, int fallbackOrdinal)
    {
        HwInfoSensor? named = rates.FirstOrDefault(s => Says(s, direction));
        if (named is not null)
            return Value(named);

        return rates.Any(s => Says(s, opposite)) ? null : Value(rates.ElementAtOrDefault(fallbackOrdinal));
    }

    /// <summary>The rate of every drive in one direction, summed.</summary>
    private static double? DriveRates(IReadOnlyList<HwInfoSensor> sensors, string[] direction, string[] opposite,
        int fallbackOrdinal)
    {
        double? total = null;
        foreach (IGrouping<(uint, uint), HwInfoSensor> drive in sensors
                     .Where(s => IsRate(s) && Components.Of(s, sensors) == Components.Storage)
                     .GroupBy(s => (s.SensorId, s.SensorInstance)))
        {
            if (Directed(drive.ToList(), direction, opposite, fallbackOrdinal) is { } rate)
                total = (total ?? 0) + rate;
        }

        return total;
    }

    /// <summary>Whether the label or the unit names one of <paramref name="words"/> as a whole word
    /// ("Current DL rate", "Bytes/sec (up)").</summary>
    private static bool Says(HwInfoSensor sensor, string[] words) =>
        words.Any(word => HasWord(Label(sensor), word) || HasWord(sensor.Unit, word));

    private static bool HasWord(string text, string word) =>
        Regex.IsMatch(text, $@"(?<![A-Za-z]){Regex.Escape(word)}(?![A-Za-z])", RegexOptions.IgnoreCase);

    private static double? Named(IEnumerable<HwInfoSensor> sensors, string label) =>
        Value(sensors.FirstOrDefault(s => Label(s).Equals(label, StringComparison.OrdinalIgnoreCase)));

    private static double? First(IEnumerable<HwInfoSensor> sensors) => Value(sensors.FirstOrDefault());

    private static double? Max(IEnumerable<HwInfoSensor> sensors)
    {
        double? max = null;
        foreach (HwInfoSensor sensor in sensors)
        {
            if (Value(sensor) is { } value && (max is null || value > max))
                max = value;
        }

        return max;
    }

    /// <summary>The reading's native value, or null when it has none.</summary>
    private static double? Value(HwInfoSensor? sensor)
    {
        if (sensor is null)
            return null;

        double value = SensorMetrics.NativeValue(sensor);
        return double.IsNaN(value) ? null : value;
    }

    [GeneratedRegex(@"^Core \d+ Clock")]
    private static partial Regex CoreClock();
}
