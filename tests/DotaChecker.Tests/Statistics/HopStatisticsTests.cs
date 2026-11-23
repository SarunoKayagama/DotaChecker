using System.Net;
using DotaChecker.Core.Diagnostics;
using DotaChecker.Core.Statistics;

namespace DotaChecker.Tests.Statistics;

public sealed class HopStatisticsTests
{
    private static readonly IPAddress Responder = IPAddress.Parse("192.0.2.1");

    [Fact]
    public void RecordsLossRttAndJitter()
    {
        var statistics = new HopStatistics();

        statistics.Record(Reply(10));
        statistics.Record(Reply(20));
        statistics.Record(Reply(30));
        statistics.Record(Timeout());

        var snapshot = statistics.Snapshot();
        snapshot.Sent.Should().Be(4);
        snapshot.Received.Should().Be(3);
        snapshot.Timeouts.Should().Be(1);
        snapshot.LossPct.Should().Be(25);
        snapshot.AverageMs.Should().Be(20);
        snapshot.BestMs.Should().Be(10);
        snapshot.WorstMs.Should().Be(30);
        snapshot.LastMs.Should().Be(30);
        snapshot.JitterMs.Should().Be(10);
    }

    [Fact]
    public void JitterSkipsLostProbes()
    {
        var statistics = new HopStatistics();

        statistics.Record(Reply(10));
        statistics.Record(Timeout());
        statistics.Record(Reply(30));

        statistics.Snapshot().JitterMs.Should().Be(20);
    }

    [Fact]
    public void RingBufferIsBoundedAndKeepsLatestSamples()
    {
        var statistics = new HopStatistics(historyCapacity: 5);

        for (var i = 1; i <= 100; i++)
        {
            statistics.Record(Reply(i));
        }

        var snapshot = statistics.Snapshot();
        snapshot.RttHistory.Should().HaveCount(5);
        snapshot.RttHistory.Should().Equal(96, 97, 98, 99, 100);
        snapshot.Sent.Should().Be(100);
    }

    [Fact]
    public void ResetClearsCountersAndHistory()
    {
        var statistics = new HopStatistics();
        statistics.Record(Reply(12));
        statistics.Record(Timeout());

        statistics.Reset();

        var snapshot = statistics.Snapshot();
        snapshot.Sent.Should().Be(0);
        snapshot.Received.Should().Be(0);
        snapshot.RttHistory.Should().BeEmpty();
    }

    private static ProbeReply Reply(double rttMs) =>
        new(Responder, ProbeOutcome.TimeExceeded, rttMs, "TtlExpired");

    private static ProbeReply Timeout() =>
        new(Responder, ProbeOutcome.NoReply, 0, "TimedOut");
}
