namespace DotaChecker.Core.Statistics;

/// <summary>Окно наблюдений: число проб и последовательность ответов для bootstrap-анализа.</summary>
public sealed record HopWindowStats(
    int HopIndex,
    long Sent,
    long Received,
    IReadOnlyList<bool> Successes);

public sealed record WindowSnapshot(
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    bool IsComplete,
    IReadOnlyDictionary<int, HopWindowStats> Hops,
    HopWindowStats E2e);

/// <summary>
/// Накопление скользящих окон фиксированной длительности. Число окон ограничено,
/// поэтому длительный мониторинг не приводит к росту памяти.
/// </summary>
public sealed class WindowAggregator
{
    private readonly object _gate = new();
    private readonly TimeSpan _duration;
    private readonly int _windowCount;
    private readonly List<WindowSnapshot> _completed = new();

    private DateTimeOffset _currentStart = DateTimeOffset.UtcNow;
    private readonly Dictionary<int, ProbeAccumulator> _currentHops = new();
    private readonly ProbeAccumulator _currentE2e = new(0);

    public WindowAggregator(TimeSpan duration, int windowCount = 3)
    {
        if (duration <= TimeSpan.Zero || windowCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        _duration = duration;
        _windowCount = Math.Max(1, windowCount);
    }

    public void RecordHop(int hopIndex, bool success, DateTimeOffset now)
    {
        lock (_gate)
        {
            RotateIfNeeded(now);
            GetOrCreate(_currentHops, hopIndex).Record(success);
        }
    }

    public void RecordE2e(bool success, DateTimeOffset now)
    {
        lock (_gate)
        {
            RotateIfNeeded(now);
            _currentE2e.Record(success);
        }
    }

    public IReadOnlyList<WindowSnapshot> Snapshots()
    {
        lock (_gate)
        {
            var result = new List<WindowSnapshot>(_completed) { CurrentSnapshot(DateTimeOffset.UtcNow) };
            return result;
        }
    }

    public void RotateNow(DateTimeOffset now)
    {
        lock (_gate)
        {
            RotateIfNeeded(now);
        }
    }

    public void Reset(DateTimeOffset now)
    {
        lock (_gate)
        {
            _completed.Clear();
            _currentHops.Clear();
            _currentE2e.Reset();
            _currentStart = now;
        }
    }

    private void RotateIfNeeded(DateTimeOffset now)
    {
        if (now - _currentStart < _duration)
        {
            return;
        }

        _completed.Add(CurrentSnapshot(now));
        while (_completed.Count >= _windowCount)
        {
            _completed.RemoveAt(0);
        }

        _currentStart = now;
        _currentHops.Clear();
        _currentE2e.Reset();
    }

    private WindowSnapshot CurrentSnapshot(DateTimeOffset now) =>
        new(
            _currentStart,
            now,
            false,
            _currentHops.ToDictionary(pair => pair.Key, pair => pair.Value.ToStats(pair.Key)),
            _currentE2e.ToStats(0));

    private static ProbeAccumulator GetOrCreate(Dictionary<int, ProbeAccumulator> target, int index)
    {
        if (!target.TryGetValue(index, out var accumulator))
        {
            accumulator = new ProbeAccumulator(index);
            target[index] = accumulator;
        }

        return accumulator;
    }

    private sealed class ProbeAccumulator
    {
        private readonly List<bool> _successes = new();

        public ProbeAccumulator(int hopIndex) => HopIndex = hopIndex;

        public int HopIndex { get; }

        public void Record(bool success) => _successes.Add(success);

        public void Reset() => _successes.Clear();

        public HopWindowStats ToStats(int index) =>
            new(index, _successes.Count, _successes.Count(success => success), _successes.ToArray());
    }
}
