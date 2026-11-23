using DotaChecker.Core.Game;

namespace DotaChecker.Network.Monitoring;

public interface IGameEndpointMonitoringService : IAsyncDisposable
{
    event Action<IReadOnlyList<EndpointMonitoringSummary>>? Updated;

    string Status { get; }

    bool IsRunning { get; }

    EndpointMonitoringSummary? Current { get; }

    IReadOnlyList<EndpointMonitoringSummary> Summaries();

    Task StartAsync(GameEndpointRecord endpoint);

    Task StopAsync(string? reason = null);
}
