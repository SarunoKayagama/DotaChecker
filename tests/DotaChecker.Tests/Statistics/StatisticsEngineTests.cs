using System.Net;
using DotaChecker.Core.Diagnostics;
using DotaChecker.Core.Models;
using DotaChecker.Core.Statistics;

namespace DotaChecker.Tests.Statistics;

public sealed class StatisticsEngineTests
{
    private static readonly IPAddress Target = IPAddress.Parse("198.51.100.1");

    [Fact]
    public void SessionStatisticsAccumulateWhileVersionStatisticsAreSeparated()
    {
        var engine = new StatisticsEngine();
        var first = new RouteVersion(1, DateTimeOffset.UtcNow, CreateRoute("192.0.2.1"), true);
        engine.BeginNewVersion(first);
        engine.RecordHop(1, Reply(10));
        engine.RecordHop(1, Reply(12));

        var second = new RouteVersion(2, DateTimeOffset.UtcNow, CreateRoute("192.0.2.9"), true);
        first.IsActive = false;
        engine.BeginNewVersion(second);
        engine.RecordHop(1, Reply(40));

        engine.CurrentSnapshots()[1].Sent.Should().Be(1);
        engine.CurrentSnapshots()[1].AverageMs.Should().Be(40);
        engine.SessionSnapshots()[1].Sent.Should().Be(3);
        engine.Versions().Should().ContainSingle();
        engine.Versions()[0].Version.Id.Should().Be(1);
        engine.Versions()[0].Hops[1].Snapshot().Sent.Should().Be(2);
    }

    [Fact]
    public void E2eIsRecordedSeparately()
    {
        var engine = new StatisticsEngine();
        engine.RecordE2e(Reply(25));
        engine.RecordHop(1, Reply(10));

        engine.E2eSnapshot().Sent.Should().Be(1);
        engine.E2eSnapshot().AverageMs.Should().Be(25);
        engine.CurrentSnapshots()[1].Sent.Should().Be(1);
    }

    [Fact]
    public void ResetAllClearsEveryBucket()
    {
        var engine = new StatisticsEngine();
        var version = new RouteVersion(1, DateTimeOffset.UtcNow, CreateRoute("192.0.2.1"), true);
        engine.BeginNewVersion(version);
        engine.RecordHop(1, Reply(10));
        engine.RecordE2e(Reply(20));

        engine.ResetAll();

        engine.CurrentSnapshots()[1].Sent.Should().Be(0);
        engine.SessionSnapshots()[1].Sent.Should().Be(0);
        engine.E2eSnapshot().Sent.Should().Be(0);
    }

    private static RouteSnapshot CreateRoute(string hop) =>
        new(Target, new[] { new RouteHop(1, new[] { IPAddress.Parse(hop) }, true) }, DateTimeOffset.UtcNow);

    private static ProbeReply Reply(double rttMs) =>
        new(IPAddress.Parse("203.0.113.5"), ProbeOutcome.TimeExceeded, rttMs, "TtlExpired");
}
