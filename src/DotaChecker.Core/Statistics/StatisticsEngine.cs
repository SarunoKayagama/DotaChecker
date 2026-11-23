using DotaChecker.Core.Diagnostics;
using DotaChecker.Core.Models;

namespace DotaChecker.Core.Statistics;

/// <summary>Замороженная статистика одной версии маршрута.</summary>
public sealed record VersionStatistics(RouteVersion Version, IReadOnlyDictionary<int, HopStatistics> Hops);

/// <summary>
/// Хранит три набора данных: статистику текущей версии, накопительную за сессию
/// и отдельный e2e reference stream. E2e — только дополнительный сигнал.
/// </summary>
public sealed class StatisticsEngine
{
    private const int MaxStoredVersions = 20;

    private readonly object _gate = new();
    private readonly Dictionary<int, HopStatistics> _session = new();
    private readonly HopStatistics _e2e = new();
    private readonly List<VersionStatistics> _versions = new();
    private readonly WindowAggregator _windows;
    private Dictionary<int, HopStatistics> _current = new();
    private RouteVersion? _currentVersion;

    public StatisticsEngine(TimeSpan? windowDuration = null, int windowCount = 3)
    {
        _windows = new WindowAggregator(windowDuration ?? TimeSpan.FromSeconds(10), windowCount);
    }

    public void RecordHop(int hopIndex, ProbeReply reply)
    {
        lock (_gate)
        {
            GetOrCreate(_session, hopIndex).Record(reply);
            GetOrCreate(_current, hopIndex).Record(reply);
        }

        _windows.RecordHop(hopIndex, IsReply(reply), DateTimeOffset.UtcNow);
    }

    public void RecordE2e(ProbeReply reply)
    {
        _e2e.Record(reply);
        _windows.RecordE2e(IsReply(reply), DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<WindowSnapshot> Windows() => _windows.Snapshots();

    public void RotateWindows(DateTimeOffset now) => Rotate(now);

    /// <summary>Начать новую версию: текущие счётчики замораживаются, сессионные продолжают расти.</summary>
    public void BeginNewVersion(RouteVersion version)
    {
        lock (_gate)
        {
            if (_current.Count > 0 && _currentVersion is not null)
            {
                _versions.Add(new VersionStatistics(_currentVersion, new Dictionary<int, HopStatistics>(_current)));
                while (_versions.Count > MaxStoredVersions)
                {
                    _versions.RemoveAt(0);
                }
            }

            _current = new Dictionary<int, HopStatistics>();
            _currentVersion = version;
        }
    }

    public IReadOnlyDictionary<int, HopStatisticsSnapshot> CurrentSnapshots()
    {
        lock (_gate)
        {
            return SnapshotLocked(_current);
        }
    }

    public IReadOnlyDictionary<int, HopStatisticsSnapshot> SessionSnapshots()
    {
        lock (_gate)
        {
            return SnapshotLocked(_session);
        }
    }

    public HopStatisticsSnapshot E2eSnapshot() => _e2e.Snapshot();

    public IReadOnlyList<VersionStatistics> Versions()
    {
        lock (_gate)
        {
            return _versions.ToArray();
        }
    }

    /// <summary>Ручной сброс: очищает и текущую, сессионную и e2e статистику.</summary>
    public void ResetAll()
    {
        lock (_gate)
        {
            foreach (var statistics in _session.Values)
            {
                statistics.Reset();
            }

            foreach (var statistics in _current.Values)
            {
                statistics.Reset();
            }

            foreach (var version in _versions)
            {
                foreach (var statistics in version.Hops.Values)
                {
                    statistics.Reset();
                }
            }

            _e2e.Reset();
            _windows.Reset(DateTimeOffset.UtcNow);
        }
    }

    private void Rotate(DateTimeOffset now) => _windows.RotateNow(now);

    private static bool IsReply(ProbeReply reply) =>
        reply.Outcome is ProbeOutcome.EchoReply or ProbeOutcome.TimeExceeded or ProbeOutcome.DestinationUnreachable;

    private static HopStatistics GetOrCreate(Dictionary<int, HopStatistics> target, int hopIndex)
    {
        if (!target.TryGetValue(hopIndex, out var statistics))
        {
            statistics = new HopStatistics();
            target[hopIndex] = statistics;
        }

        return statistics;
    }

    private static IReadOnlyDictionary<int, HopStatisticsSnapshot> SnapshotLocked(
        Dictionary<int, HopStatistics> source)
    {
        return source.ToDictionary(pair => pair.Key, pair => pair.Value.Snapshot());
    }
}
