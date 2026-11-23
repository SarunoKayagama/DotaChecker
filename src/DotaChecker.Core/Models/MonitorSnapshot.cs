using System.Net;
using DotaChecker.Core.Statistics;

namespace DotaChecker.Core.Models;

/// <summary>Строка таблицы мониторинга, подготовленная для UI.</summary>
public sealed record MonitorHopView(
    int HopIndex,
    IReadOnlyList<IPAddress> Responders,
    string Hostname,
    bool IsDestination,
    HopStatisticsSnapshot Statistics)
{
    public IPAddress? PrimaryAddress => Responders.Count == 0 ? null : Responders[0];

    public string AddressText => Responders.Count == 0
        ? "—"
        : string.Join(" / ", Responders.Select(address => address.ToString()));
}

/// <summary>Снимок всего мониторинга, публикуемый раз в 700 мс.</summary>
public sealed record MonitorSnapshot(
    IPAddress Target,
    int Round,
    IReadOnlyList<MonitorHopView> Hops,
    MonitorHopView E2e,
    DateTimeOffset PublishedAt);
