using System.Diagnostics;
using DotaChecker.Core.Game;
using DotaChecker.Network.Discovery.Models;

namespace DotaChecker.Network.Discovery;

/// <summary>
/// Один ETW-провайдер на сервис, фоновый опрос с ограниченной частотой,
/// сохранение истории и пересчёт классификации. Не знает о UI.
/// </summary>
public sealed class GameEndpointDiscoveryService : IGameEndpointDiscoveryService
{
    private readonly UdpActivityProbe _probe = new();
    private readonly GameEndpointHistory _history;
    private readonly GameEndpointClassifier _classifier = new();
    private readonly UdpActivityGameEndpointSource _source;
    private readonly TimeSpan _pollInterval;

    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    public GameEndpointDiscoveryService(string historyPath, TimeSpan? pollInterval = null)
    {
        _history = new GameEndpointHistory(historyPath);
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
        _source = new UdpActivityGameEndpointSource(
            _probe,
            TryGetProcessName,
            TryGetProcessStartTime);
    }

    public event Action<IReadOnlyList<GameEndpointRecord>>? Updated;

    public EtAvailability Status => _probe.Status;

    public string StatusReason => _probe.StatusReason;

    public bool IsRunning => _cancellation is not null;

    public async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        var pids = GetDotaProcessIds();
        var status = _probe.Start(pids);
        if (status != EtAvailability.Available)
        {
            Updated?.Invoke(Records());
            return;
        }

        _cancellation = new CancellationTokenSource();
        _loop = PollAsync(_cancellation.Token);
        Updated?.Invoke(Records());
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cancellation is not null)
        {
            _cancellation.Cancel();
            try
            {
                if (_loop is not null)
                {
                    await _loop.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Нормальная остановка.
            }

            _cancellation.Dispose();
            _cancellation = null;
            _loop = null;
        }

        await _probe.StopAsync().ConfigureAwait(false);
        // Сохраняем историю и при выходе без запущенного ETW — файл должен существовать.
        _history.Save();
    }

    public IReadOnlyList<GameEndpointRecord> Records() => _history.Records();

    public bool Exclude(string key)
    {
        var result = _history.Exclude(key);
        if (result)
        {
            _history.Save();
            Updated?.Invoke(Records());
        }

        return result;
    }

    public bool Include(string key)
    {
        var result = _history.Include(key);
        if (result)
        {
            _history.Save();
            Updated?.Invoke(Records());
        }

        return result;
    }

    public void Save() => _history.Save();

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await _probe.DisposeAsync().ConfigureAwait(false);
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_pollInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            _probe.SetTrackedProcessIds(GetDotaProcessIds());
            foreach (var observation in _source.CaptureSnapshot())
            {
                _history.UpsertObservation(observation);
            }

            _history.Reclassify(_classifier);
            _history.RemoveStale(DateTimeOffset.UtcNow);
            _history.Save();
            Updated?.Invoke(Records());
        }
    }

    private static int[] GetDotaProcessIds() =>
        Process.GetProcessesByName("dota2").Select(process => process.Id).Distinct().ToArray();

    private static string TryGetProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return $"<pid {pid}>";
        }
    }

    private static DateTimeOffset? TryGetProcessStartTime(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return new DateTimeOffset(process.StartTime);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
