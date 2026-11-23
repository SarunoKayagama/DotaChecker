using System.Net;

namespace DotaChecker.Core.Models;

/// <summary>Один узел маршрута. Несколько адресов возможны при ECMP.</summary>
public sealed record RouteHop(
    int HopIndex,
    IReadOnlyList<IPAddress> Responders,
    bool IsDestination)
{
    public IPAddress? PrimaryAddress => Responders.Count == 0 ? null : Responders[0];

    public bool IsResolved => Responders.Count > 0;
}

/// <summary>Снимок маршрута, полученный discovery.</summary>
public sealed record RouteSnapshot(
    IPAddress Target,
    IReadOnlyList<RouteHop> Hops,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// Подпись маршрута. Порядок адресов внутри узла не важен — это позволяет
    /// отличить смену маршрутизатора от обычного ECMP-переключения.
    /// </summary>
    public string Signature => string.Join(
        "|",
        Hops.Select(hop => string.Join(
            ",",
            hop.Responders.Select(ip => ip.ToString()).OrderBy(text => text, StringComparer.Ordinal))));
}

/// <summary>Версия маршрута: статистика разных версий не смешивается.</summary>
public sealed class RouteVersion
{
    public RouteVersion(int id, DateTimeOffset startedAt, RouteSnapshot snapshot, bool isActive)
    {
        Id = id;
        StartedAt = startedAt;
        Snapshot = snapshot;
        IsActive = isActive;
    }

    public int Id { get; }

    public DateTimeOffset StartedAt { get; }

    public RouteSnapshot Snapshot { get; }

    public bool IsActive { get; set; }
}

public enum RouteChangeKind
{
    RouteReplaced,
    EcmpVariation,
    DestinationChanged,
}

public sealed record RouteChangeEvent(
    DateTimeOffset OccurredAt,
    RouteChangeKind Kind,
    string Description);
