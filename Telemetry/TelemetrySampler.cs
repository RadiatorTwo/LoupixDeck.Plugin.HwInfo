namespace LoupixDeck.Plugin.HwInfo.Telemetry;

/// <summary>
/// Samples the HWiNFO snapshot once a second into a <see cref="TelemetryFrame"/>: every sensor and
/// every <see cref="PageMetrics"/> metric gets a 72-sample history (one chart column per second),
/// clock and load readings are smoothed with an EMA (α 0.4) so noise doesn't read as motion, and
/// alert states are evaluated with hysteresis. Runs on its own timer, off the host's render lock;
/// render calls only read the latest published frame.
/// </summary>
internal sealed class TelemetrySampler(HwInfoService service, Func<TelemetrySettings> readSettings) : IDisposable
{
    /// <summary>Samples kept per metric — the width of the design's 72-px chart.</summary>
    public const int HistoryLength = 72;

    private const double EmaAlpha = 0.4;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Track> _tracks = [];
    private Timer? _timer;
    private volatile TelemetryFrame _frame = TelemetryFrame.Unavailable;

    public TelemetryFrame Frame => _frame;

    public void Start()
    {
        _timer ??= new Timer(_ => Sample(), null, TimeSpan.Zero, Interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();

    private void Sample()
    {
        try
        {
            lock (_gate)
                _frame = BuildFrame();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TelemetrySampler: sample failed ({ex.Message}).");
        }
    }

    private TelemetryFrame BuildFrame()
    {
        if (!service.IsAvailable)
        {
            _tracks.Clear();
            return TelemetryFrame.Unavailable;
        }

        IReadOnlyList<HwInfoSensor> sensors = service.Sensors;
        TelemetrySettings settings = readSettings();
        double tj = settings.TjMax;

        List<(string Key, double Value, MetricInfo Info)> inputs = [];
        foreach (HwInfoSensor sensor in sensors)
        {
            inputs.Add((MetricKeys.ForSensor(sensor), SensorMetrics.NativeValue(sensor),
                SensorMetrics.Describe(sensor, Components.Of(sensor, sensors), tj)));
        }

        foreach (PageMetrics.Definition definition in PageMetrics.All)
        {
            if (definition.Read(sensors) is { } value)
                inputs.Add((definition.Id, value, definition.Describe(tj)));
        }

        // Fan rules read the temperature state they watch, so temperatures go first.
        Dictionary<string, MetricSnapshot> metrics = [];
        foreach ((string key, double value, MetricInfo info) in inputs.OrderBy(i => IsFanRule(i.Info.Threshold)))
        {
            if (!_tracks.TryGetValue(key, out Track? track))
                _tracks[key] = track = new Track();

            double smoothed = info.Smooth && !double.IsNaN(track.Last)
                ? track.Last + (EmaAlpha * (value - track.Last))
                : value;
            track.Push(smoothed);

            MetricState companion = info.Threshold switch
            {
                ThresholdKind.CpuFanStall => metrics.GetValueOrDefault(PageMetrics.CpuTemp)?.State ?? MetricState.Ok,
                ThresholdKind.GpuFanStall => metrics.GetValueOrDefault(PageMetrics.GpuTemp)?.State ?? MetricState.Ok,
                _ => MetricState.Ok
            };
            track.State = Thresholds.Evaluate(info.Threshold, smoothed, settings, track.State, companion);

            double[] history = track.ToArray();
            double max = info.GrowToPeak ? Math.Max(info.Max, Peak(history)) : info.Max;
            metrics[key] = new MetricSnapshot(smoothed, track.State, history, info.Min, max,
                Thresholds.LimitsFor(info.Threshold, settings)?.Warn, info.Format, info.Unit);
        }

        // A metric that vanished (sensor or hardware gone in HWiNFO) keeps a gap in its chart and is dropped
        // once its whole history has scrolled out.
        foreach ((string key, Track track) in _tracks.ToArray())
        {
            if (metrics.ContainsKey(key))
                continue;

            track.Push(double.NaN);
            if (++track.Missing >= HistoryLength)
                _tracks.Remove(key);
        }

        return new TelemetryFrame(true, sensors, metrics);
    }

    private static bool IsFanRule(ThresholdKind kind) =>
        kind is ThresholdKind.CpuFanStall or ThresholdKind.GpuFanStall;

    private static double Peak(double[] history)
    {
        double peak = double.MinValue;
        foreach (double value in history)
        {
            if (!double.IsNaN(value) && value > peak)
                peak = value;
        }

        return peak;
    }

    /// <summary>Ring buffer of one metric's last <see cref="HistoryLength"/> samples.</summary>
    private sealed class Track
    {
        private readonly double[] _samples = new double[HistoryLength];
        private int _count;
        private int _next;

        public double Last { get; private set; } = double.NaN;

        public MetricState State { get; set; }

        public int Missing { get; set; }

        public void Push(double value)
        {
            _samples[_next] = value;
            _next = (_next + 1) % HistoryLength;
            _count = Math.Min(_count + 1, HistoryLength);
            Last = value;
            if (!double.IsNaN(value))
                Missing = 0;
        }

        public double[] ToArray()
        {
            double[] result = new double[_count];
            int start = (_next - _count + HistoryLength) % HistoryLength;
            for (int i = 0; i < _count; i++)
                result[i] = _samples[(start + i) % HistoryLength];

            return result;
        }
    }
}
