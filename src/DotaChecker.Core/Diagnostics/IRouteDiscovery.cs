using System.Net;
using DotaChecker.Core.Models;

namespace DotaChecker.Core.Diagnostics;

/// <summary>Обнаружение маршрута до цели. Реализация не зависит от UI.</summary>
public interface IRouteDiscovery
{
    Task<RouteSnapshot> DiscoverAsync(
        IPAddress target,
        int maxHops,
        TimeSpan probeTimeout,
        CancellationToken cancellationToken);
}
