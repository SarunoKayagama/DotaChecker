using System.Net;
using DotaChecker.Core.Configuration;
using DotaChecker.Core.Models;
using DotaChecker.Core.Statistics;
using DotaChecker.Network.Dns;
using DotaChecker.Network.Icmp;
using DotaChecker.Network.Monitoring;

namespace DotaChecker.Tests.Icmp;

[Trait("Category", "Integration")]
public sealed class ContinuousProbeEngineTests
{
    [Fact]
    public async Task Engine_ProducesSnapshotsAccumulatesAndCapsHistory()
    {
        var settings = new AppSettings
        {
            ProbeIntervalMs = 200,
            ProbeTimeoutMs = 300,
            RouteRefreshSeconds = 600,
            MaxHops = 8,
            ReverseDnsEnabled = false,
            MaxConcurrentProbes = 8,
        };

        await using var transport = new IcmpProbeTransport();
        using var dns = new DnsResolver();
        var statistics = new StatisticsEngine();
        var discovery = new IcmpRouteDiscovery(transport, queriesPerHop: 1, slotBase: 100);
        await using var engine = new ContinuousProbeEngine(transport, discovery, statistics, settings, dns);

        MonitorSnapshot? last = null;
        engine.SnapshotPublished += snapshot => last = snapshot;

        await engine.StartAsync(IPAddress.Parse("8.8.8.8"));
        await Task.Delay(2500);
        await engine.StopAsync();

        last.Should().NotBeNull();
        last!.Hops.Should().NotBeEmpty();
        last.Hops.Should().OnlyContain(hop => hop.Statistics.Sent > 0);
        last.E2e.Statistics.Sent.Should().BeGreaterThan(0);
        engine.Round.Should().BeGreaterThan(0);
        statistics.SessionSnapshots().Values.Should().OnlyContain(snapshot => snapshot.RttHistory.Count <= 600);

        engine.ResetStatistics();
        statistics.SessionSnapshots().Values.Should().OnlyContain(snapshot => snapshot.Sent == 0);
        statistics.E2eSnapshot().Sent.Should().Be(0);
    }
}
