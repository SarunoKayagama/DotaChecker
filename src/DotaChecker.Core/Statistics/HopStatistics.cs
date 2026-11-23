using DotaChecker.Core.Diagnostics;

namespace DotaChecker.Core.Statistics;

/// <summary>Иммутабельный снимок статистики узла.</summary>
public sealed record HopStatisticsSnapshot(
    long Sent,
    long Received,
    long Timeouts,
    double LossPct,
    double LastMs,
    double AverageMs,
    double BestMs,
    double WorstMs,
    double JitterMs,
    IReadOnlyList<double> RttHistory,
    IReadOnlyList<bool> SuccessHistory)
{
    public bool HasReplies => Received > 0;
}

/// <summary>
/// Накопительная статистика одного узла с фиксированным кольцевым буфером.
/// Потокобезопасна: запись идёт из потоков проб, чтение — из потока UI.
/// </summary>
public sealed class HopStatistics
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly double[] _rttHistory;
    private readonly bool[] _successHistory;
    private int _historyCount;
    private int _historyIndex;

    private long _sent;
    private long _received;
    private long _timeouts;
    private double _sumRtt;
    private double _minRtt = double.MaxValue;
    private double _maxRtt;
    private double _lastRtt;
    private double _sumAbsJitter;
    private long _jitterSamples;
    private double _previousRtt;
    private bool _hasPreviousRtt;

    public HopStatistics(int historyCapacity = 600)
    {
        if (historyCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(historyCapacity));
        }

        _capacity = historyCapacity;
        _rttHistory = new double[_capacity];
        _successHistory = new bool[_capacity];
    }

    public void Record(ProbeReply reply)
    {
        var isReply = reply.Outcome is
            ProbeOutcome.EchoReply or ProbeOutcome.TimeExceeded or ProbeOutcome.DestinationUnreachable;

        lock (_gate)
        {
            _sent++;

            if (isReply)
            {
                _received++;
                _lastRtt = reply.RttMs;
                _sumRtt += reply.RttMs;
                _minRtt = Math.Min(_minRtt, reply.RttMs);
                _maxRtt = Math.Max(_maxRtt, reply.RttMs);

                // Jitter по RFC 3393: средняя абсолютная разница между соседними ответами.
                // Потерянные пробы пропускаются: это не изменение RTT, а отсутствие ответа.
                if (_hasPreviousRtt)
                {
                    _sumAbsJitter += Math.Abs(reply.RttMs - _previousRtt);
                    _jitterSamples++;
                }

                _previousRtt = reply.RttMs;
                _hasPreviousRtt = true;
                _rttHistory[_historyIndex] = reply.RttMs;
                _successHistory[_historyIndex] = true;
            }
            else
            {
                _timeouts++;
                _rttHistory[_historyIndex] = double.NaN;
                _successHistory[_historyIndex] = false;
            }

            _historyIndex = (_historyIndex + 1) % _capacity;
            if (_historyCount < _capacity)
            {
                _historyCount++;
            }
        }
    }

    public HopStatisticsSnapshot Snapshot()
    {
        lock (_gate)
        {
            var rtt = new double[_historyCount];
            var success = new bool[_historyCount];

            for (var i = 0; i < _historyCount; i++)
            {
                var index = (_historyIndex - _historyCount + i + _capacity) % _capacity;
                rtt[i] = _rttHistory[index];
                success[i] = _successHistory[index];
            }

            return new HopStatisticsSnapshot(
                _sent,
                _received,
                _timeouts,
                _sent == 0 ? 0 : 100.0 * _timeouts / _sent,
                _lastRtt,
                _received == 0 ? 0 : _sumRtt / _received,
                _received == 0 ? 0 : _minRtt,
                _maxRtt,
                _jitterSamples == 0 ? 0 : _sumAbsJitter / _jitterSamples,
                rtt,
                success);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            Array.Clear(_rttHistory);
            Array.Clear(_successHistory);
            _historyCount = 0;
            _historyIndex = 0;
            _sent = 0;
            _received = 0;
            _timeouts = 0;
            _sumRtt = 0;
            _minRtt = double.MaxValue;
            _maxRtt = 0;
            _lastRtt = 0;
            _sumAbsJitter = 0;
            _jitterSamples = 0;
            _previousRtt = 0;
            _hasPreviousRtt = false;
        }
    }
}
