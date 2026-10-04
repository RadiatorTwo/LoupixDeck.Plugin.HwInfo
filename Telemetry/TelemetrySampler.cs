using System.Collections.Concurrent;

namespace LoupixDeck.Plugin.HwInfo.Telemetry;

/// <summary>
/// Samples the HWiNFO snapshot once a second into a <see cref="TelemetryFrame"/>: every
/// <see cref="PageMetrics"/> metric, and every sensor a tile has asked for, gets a 72-sample
/// history (one chart column per second),
/// clock and load readings are smoothed with an EMA (α 0.4) so noise doesn't read as motion, and
/// alert states are evaluated with hysteresis. Runs on its own timer, off the host's render lock;
/// render calls only read the latest published frame.
/// </summary>
internal sealed class TelemetrySampler(HwInfoService service, Func<TelemetrySettings> readSettings,
    Action<string> log) : IDisposable
{
    /// <summary>Samples kept per metric — the width of the design's 72-px chart.</summary>
    public const int HistoryLength = 72;

    private const double EmaAlpha = 0.4;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Track> _tracks = new(StringComparer.Ordinal);

    // Metric keys a tile has asked for (TelemetryFrame.Get); written by render threads.
    private readonly ConcurrentDictionary<string, byte> _requested = new(StringComparer.Ordinal);

    // The requested sensor keys by HWiNFO triple, so a sample finds them without formatting a key
    // per reading. Rebuilt when a key was added (keys are never removed).
    private readonly Dictionary<(uint, uint, uint), string> _requestedSensors = [];
    private int _requestedCount;

    // Reused on every sample, which runs under _gate.
    private readonly List<(string Key, double Value, MetricInfo Info)> _inputs = [];
    private readonly List<string> _gone = [];
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
            log($"TelemetrySampler: sample failed ({ex.Message}).");
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

        // Single sensors only once a tile has asked for them; the page metrics always, since the
        // pages and the fan rules read them.
        _inputs.Clear();
        if (_requested.Count != _requestedCount)
            IndexRequestedSensors();

        if (_requestedSensors.Count > 0)
        {
            foreach (HwInfoSensor sensor in sensors)
            {
                if (_requestedSensors.TryGetValue((sensor.SensorId, sensor.SensorInstance, sensor.ReadingId),
                        out string? key))
                {
                    _inputs.Add((key, SensorMetrics.NativeValue(sensor),
                        SensorMetrics.Describe(sensor, Components.Of(sensor, sensors), tj)));
                }
            }
        }

        foreach (PageMetrics.Definition definition in PageMetrics.All)
        {
            if (definition.Read(sensors) is { } value)
                _inputs.Add((definition.Id, value, definition.Describe(tj)));
        }

        // Fan rules read the temperature state they watch, so temperatures go first.
        Dictionary<string, MetricSnapshot> metrics = new(_inputs.Count, StringComparer.Ordinal);
        foreach ((string key, double value, MetricInfo info) in _inputs)
        {
            if (!IsFanRule(info.Threshold))
                Update(key, value, info, settings, metrics);
        }

        foreach ((string key, double value, MetricInfo info) in _inputs)
        {
            if (IsFanRule(info.Threshold))
                Update(key, value, info, settings, metrics);
        }

        // A metric that vanished (sensor or hardware gone in HWiNFO) keeps a gap in its chart and is dropped
        // once its whole history has scrolled out.
        _gone.Clear();
        foreach ((string key, Track track) in _tracks)
        {
            if (metrics.ContainsKey(key))
                continue;

            track.Push(double.NaN);
            if (++track.Missing >= HistoryLength)
                _gone.Add(key);
        }

        foreach (string key in _gone)
            _tracks.Remove(key);

        return new TelemetryFrame(true, sensors, metrics, _requested);
    }

    /// <summary>Maps every requested key that names a sensor (<see cref="HwInfoSensorRef"/>) to its
    /// triple; page metric ids and unknown keys are skipped.</summary>
    private void IndexRequestedSensors()
    {
        _requestedSensors.Clear();
        foreach (string key in _requested.Keys)
        {
            if (HwInfoSensorRef.TryParse(key, out (uint, uint, uint) id))
                _requestedSensors[id] = key;
        }

        _requestedCount = _requested.Count;
    }

    private void Update(string key, double value, MetricInfo info, TelemetrySettings settings,
        Dictionary<string, MetricSnapshot> metrics)
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

        ReadOnlyMemory<double> history = track.History();
        double max = info.GrowToPeak ? Math.Max(info.Max, Peak(history.Span)) : info.Max;
        // Only the text changes with the unit; value, history, bar and limits stay in °C.
        MetricFormat format = settings.Fahrenheit && info.Format == MetricFormat.Temperature
            ? MetricFormat.TemperatureFahrenheit
            : info.Format;
        metrics[key] = new MetricSnapshot(smoothed, track.State, history, info.Min, max,
            Thresholds.LimitsFor(info.Threshold, settings)?.Warn, format, info.Unit);
    }

    private static bool IsFanRule(ThresholdKind kind) =>
        kind is ThresholdKind.CpuFanStall or ThresholdKind.GpuFanStall;

    private static double Peak(ReadOnlySpan<double> history)
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

        // The history handed to a frame, oldest to newest. Two buffers taken in turn: a frame's
        // buffer is rewritten only two samples later, long after every render has moved on to a
        // newer frame, so no sample allocates and no render reads a buffer being written.
        private readonly double[][] _views = [new double[HistoryLength], new double[HistoryLength]];
        private int _view;
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

        public ReadOnlyMemory<double> History()
        {
            _view ^= 1;
            double[] result = _views[_view];
            int start = (_next - _count + HistoryLength) % HistoryLength;
            for (int i = 0; i < _count; i++)
                result[i] = _samples[(start + i) % HistoryLength];

            return result.AsMemory(0, _count);
        }
    }
}
