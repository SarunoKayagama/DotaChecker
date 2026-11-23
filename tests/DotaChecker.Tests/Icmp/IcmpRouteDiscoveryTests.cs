using System.Net;
using DotaChecker.Core.Diagnostics;
using DotaChecker.Network.Icmp;

namespace DotaChecker.Tests.Icmp;

public sealed class IcmpRouteDiscoveryTests
{
    private static readonly IPAddress Target = IPAddress.Parse("198.51.100.10");

    [Fact]
    public async Task DiscoverAsync_StopsAtDestination()
    {
        var transport = new FakeTransport(request => request.Ttl switch
        {
            1 => Reply(request, "192.0.2.1", ProbeOutcome.TimeExceeded),
            2 => Reply(request, "198.51.100.10", ProbeOutcome.EchoReply),
            _ => Reply(request, "192.0.2.254", ProbeOutcome.TimeExceeded),
        });
        var discovery = new IcmpRouteDiscovery(transport, queriesPerHop: 1);

        var route = await discovery.DiscoverAsync(Target, 10, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        route.Hops.Should().HaveCount(2);
        route.Hops[0].PrimaryAddress.Should().Be(IPAddress.Parse("192.0.2.1"));
        route.Hops[0].IsDestination.Should().BeFalse();
        route.Hops[1].PrimaryAddress.Should().Be(Target);
        route.Hops[1].IsDestination.Should().BeTrue();
        transport.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task DiscoverAsync_CollectsEcmpRespondersAtSameHop()
    {
        var counter = 0;
        var transport = new FakeTransport(request =>
        {
            if (request.Ttl == 1)
            {
                counter++;
                var address = counter % 2 == 1 ? "192.0.2.1" : "192.0.2.2";
                return Reply(request, address, ProbeOutcome.TimeExceeded);
            }

            return Reply(request, Target.ToString(), ProbeOutcome.EchoReply);
        });
        var discovery = new IcmpRouteDiscovery(transport, queriesPerHop: 4);

        var route = await discovery.DiscoverAsync(Target, 10, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        route.Hops[0].Responders.Should().HaveCount(2);
        route.Signature.Should().Contain("192.0.2.1,192.0.2.2");
    }

    [Fact]
    public async Task DiscoverAsync_StopsAfterConsecutiveSilentHops()
    {
        var transport = new FakeTransport(request => Reply(request, Target.ToString(), ProbeOutcome.NoReply));
        var discovery = new IcmpRouteDiscovery(transport, queriesPerHop: 1, maxConsecutiveSilentHops: 3);

        var route = await discovery.DiscoverAsync(Target, 30, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        route.Hops.Should().HaveCount(3);
        route.Hops.Should().OnlyContain(hop => !hop.IsResolved);
    }

    [Fact]
    public async Task DiscoverAsync_TreatsUnreachableAsDestination()
    {
        var transport = new FakeTransport(request => request.Ttl == 1
            ? Reply(request, "192.0.2.1", ProbeOutcome.TimeExceeded)
            : Reply(request, Target.ToString(), ProbeOutcome.DestinationUnreachable));
        var discovery = new IcmpRouteDiscovery(transport, queriesPerHop: 1);

        var route = await discovery.DiscoverAsync(Target, 10, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        route.Hops[1].IsDestination.Should().BeTrue();
        route.Hops[1].PrimaryAddress.Should().Be(Target);
    }

    private static ProbeReply Reply(ProbeRequest request, string address, ProbeOutcome outcome) =>
        new(IPAddress.Parse(address), outcome, 5, outcome.ToString());

    private sealed class FakeTransport : IProbeTransport
    {
        private readonly Func<ProbeRequest, ProbeReply> _handler;

        public FakeTransport(Func<ProbeRequest, ProbeReply> handler) => _handler = handler;

        public List<ProbeRequest> Requests { get; } = new();

        public Task<ProbeReply> SendAsync(ProbeRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_handler(request));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
