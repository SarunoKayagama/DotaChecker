using System.Net;
using DotaChecker.Core.Diagnostics;
using DotaChecker.Core.Models;

namespace DotaChecker.Network.Icmp;

/// <summary>
/// ICMP traceroute: TTL от 1 до maxHops. На каждом TTL отправляется несколько проб,
/// чтобы собрать все отвечающие адреса (ECMP) и не делать вывод по одному пакету.
/// </summary>
public sealed class IcmpRouteDiscovery : IRouteDiscovery
{
    private readonly IProbeTransport _transport;
    private readonly int _queriesPerHop;
    private readonly int _slotBase;
    private readonly int _maxConsecutiveSilentHops;

    public IcmpRouteDiscovery(
        IProbeTransport transport,
        int queriesPerHop = 3,
        int slotBase = 100,
        int maxConsecutiveSilentHops = 3)
    {
        _transport = transport;
        _queriesPerHop = Math.Max(1, queriesPerHop);
        _slotBase = slotBase;
        _maxConsecutiveSilentHops = Math.Max(1, maxConsecutiveSilentHops);
    }

    public async Task<RouteSnapshot> DiscoverAsync(
        IPAddress target,
        int maxHops,
        TimeSpan probeTimeout,
        CancellationToken cancellationToken)
    {
        var hops = new List<RouteHop>();
        var silentHops = 0;

        for (var ttl = 1; ttl <= maxHops; ttl++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var responders = new HashSet<IPAddress>();
            var destinationReached = false;

            for (var query = 0; query < _queriesPerHop; query++)
            {
                var reply = await _transport.SendAsync(
                        new ProbeRequest(_slotBase + ttl, target, ttl, probeTimeout),
                        cancellationToken)
                    .ConfigureAwait(false);

                switch (reply.Outcome)
                {
                    case ProbeOutcome.TimeExceeded:
                        responders.Add(reply.Responder);
                        break;

                    case ProbeOutcome.EchoReply:
                        responders.Add(reply.Responder);
                        destinationReached = true;
                        break;

                    case ProbeOutcome.DestinationUnreachable:
                        destinationReached = true;
                        break;
                }
            }

            if (destinationReached && responders.Count == 0)
            {
                responders.Add(target);
            }

            if (responders.Count == 0)
            {
                hops.Add(new RouteHop(ttl, Array.Empty<IPAddress>(), false));
                silentHops++;
                if (silentHops >= _maxConsecutiveSilentHops)
                {
                    break;
                }

                continue;
            }

            silentHops = 0;
            hops.Add(new RouteHop(
                ttl,
                responders.OrderBy(address => address.ToString(), StringComparer.Ordinal).ToArray(),
                destinationReached));

            if (destinationReached)
            {
                break;
            }
        }

        return new RouteSnapshot(target, hops, DateTimeOffset.UtcNow);
    }
}
