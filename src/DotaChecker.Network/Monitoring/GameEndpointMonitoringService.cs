using System.Net;
using DotaChecker.Core.Game;
using DotaChecker.Core.Models;
using DotaChecker.Network.Discovery;

namespace DotaChecker.Network.Monitoring;

/// <summary>
/// Управляет одним активным ICMP-мониторингом игрового endpoint через существующий
/// ContinuousProbeEngine. Не создаёт второй движок и вторую статистику.
/// </summary>
public sealed class GameEndpointMonitoringService : IGameEndpointMonitoringService
{
    private readonly ContinuousProbeEngine _engine;
    private readonly EndpointMonitoringHistory _history;
    private readonly IGameEndpointDiscoveryService _discovery;

    private string? _currentKey;
    private string _status = "Мониторинг не запущен.";

    public GameEndpointMonitoringService(
        ContinuousProbeEngine engine,
        EndpointMonitoringHistory history,
        IGameEndpointDiscoveryService discovery)
    {
        _engine = engine;
        _history = history;
        _discovery = discovery;
        _engine.SnapshotPublished += OnSnapshot;
        _discovery.Updated += OnEndpointsUpdated;
    }

    public event Action<IReadOnlyList<EndpointMonitoringSummary>>? Updated;

    public string Status => _status;

    public bool IsRunning => _engine.IsRunning && _currentKey is not null;

    public EndpointMonitoringSummary? Current =>
        _currentKey is null
            ? null
            : _history.Summaries().FirstOrDefault(item => item.EndpointKey == _currentKey);

    public IReadOnlyList<EndpointMonitoringSummary> Summaries() => _history.Summaries();

    public async Task StartAsync(GameEndpointRecord endpoint)
    {
        if (IsRunning && _currentKey == endpoint.Key)
        {
            return;
        }

        if (_engine.IsRunning)
        {
            await _engine.StopAsync().ConfigureAwait(false);
        }

        _engine.ResetStatistics();
        var route = new RouteSnapshot(
            endpoint.RemoteAddress,
            new[] { new RouteHop(1, new[] { endpoint.RemoteAddress }, true) },
            DateTimeOffset.UtcNow);

        _currentKey = endpoint.Key;
        _status = $"Мониторинг {endpoint.RemoteAddress}: запуск...";
        await _engine.StartWithRouteAsync(route).ConfigureAwait(false);
        _status = $"Мониторинг {endpoint.RemoteAddress}: активен (ICMP, не UDP).";
        Publish();
    }

    public async Task StopAsync(string? reason = null)
    {
        if (_engine.IsRunning)
        {
            await _engine.StopAsync().ConfigureAwait(false);
        }

        if (_currentKey is not null)
        {
            _status = reason ?? "Мониторинг остановлен.";
            _currentKey = null;
        }

        _history.Save();
        Publish();
    }

    public async ValueTask DisposeAsync()
    {
        _engine.SnapshotPublished -= OnSnapshot;
        _discovery.Updated -= OnEndpointsUpdated;
        await StopAsync("Приложение закрыто.").ConfigureAwait(false);
    }

    private void OnSnapshot(MonitorSnapshot snapshot)
    {
        if (_currentKey is null)
        {
            return;
        }

        var stats = _engine.Statistics();
        if (!stats.TryGetValue(1, out var hop))
        {
            return;
        }

        _history.Upsert(new EndpointMonitoringSummary(
            _currentKey,
            snapshot.Target.ToString(),
            hop.Sent,
            hop.Received,
            hop.LossPct,
            hop.AverageMs,
            hop.JitterMs,
            hop.LastMs,
            hop.RttHistory.Where(value => !double.IsNaN(value)).ToArray(),
            DateTimeOffset.UtcNow,
            _status));
        Publish();
    }

    private void OnEndpointsUpdated(IReadOnlyList<GameEndpointRecord> endpoints)
    {
        if (_currentKey is null)
        {
            return;
        }

        if (endpoints.All(item => item.Key != _currentKey))
        {
            _ = StopAsync("Endpoint исчез из discovered-набора; мониторинг остановлен.");
        }
    }

    private void Publish() => Updated?.Invoke(_history.Summaries());
}
