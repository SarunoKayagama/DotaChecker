using DotaChecker.Core.Game;
using DotaChecker.Network.Discovery.Models;

namespace DotaChecker.Network.Discovery;

/// <summary>Управление ETW-обнаружением игровых endpoint и историей наблюдений.</summary>
public interface IGameEndpointDiscoveryService : IAsyncDisposable
{
    EtAvailability Status { get; }

    string StatusReason { get; }

    bool IsRunning { get; }

    event Action<IReadOnlyList<GameEndpointRecord>>? Updated;

    Task StartAsync();

    Task StopAsync();

    IReadOnlyList<GameEndpointRecord> Records();

    bool Exclude(string key);

    bool Include(string key);

    void Save();
}
