using System.Net;
using DotaChecker.Network.Discovery;
using DotaChecker.Network.Discovery.Models;

namespace DotaChecker.Tests.Discovery;

public sealed class UdpActivityAggregatorTests
{
    [Fact]
    public void AggregatesByEndpointAndKeepsFirstLastSeen()
    {
        var aggregator = new UdpActivityAggregator();
        aggregator.Record(Event(Time(1), size: 100));
        aggregator.Record(Event(Time(3), size: 50));
        aggregator.Record(Event(Time(2), size: 25));

        var snapshot = aggregator.Snapshot();

        snapshot.Should().ContainSingle();
        snapshot[0].EventCount.Should().Be(3);
        snapshot[0].SendCount.Should().Be(3);
        snapshot[0].ReceiveCount.Should().Be(0);
        snapshot[0].BytesObserved.Should().Be(175);
        snapshot[0].DataBytesObserved.Should().Be(94);
        snapshot[0].FirstSeen.Should().Be(Time(1));
        snapshot[0].LastSeen.Should().Be(Time(3));
        aggregator.TotalEvents.Should().Be(3);
    }

    [Fact]
    public void SendAndReceiveOfSameConnectionMergeIntoOneRecord()
    {
        var aggregator = new UdpActivityAggregator();
        aggregator.Record(Event(Time(1), direction: UdpDirection.Send, localPort: 65479, remotePort: 27024));
        aggregator.Record(Event(Time(2), direction: UdpDirection.Receive, localPort: 65479, remotePort: 27024));

        var snapshot = aggregator.Snapshot();

        snapshot.Should().ContainSingle();
        snapshot[0].SendCount.Should().Be(1);
        snapshot[0].ReceiveCount.Should().Be(1);
        snapshot[0].EventCount.Should().Be(2);
    }

    [Fact]
    public void DifferentRemotePortsProduceDifferentRecords()
    {
        var aggregator = new UdpActivityAggregator();
        aggregator.Record(Event(Time(1), remotePort: 27015));
        aggregator.Record(Event(Time(2), remotePort: 443));

        aggregator.Snapshot().Should().HaveCount(2);
    }

    [Fact]
    public void CapacityEvictsOldestRecords()
    {
        var aggregator = new UdpActivityAggregator(capacity: 2);
        for (var i = 0; i < 10; i++)
        {
            aggregator.Record(Event(Time(i), remotePort: 1000 + i));
        }

        aggregator.Snapshot().Should().HaveCount(2);
        aggregator.TotalEvents.Should().Be(10);
    }

    [Fact]
    public void MalformedEventsAreCountedSeparately()
    {
        var aggregator = new UdpActivityAggregator();
        aggregator.RecordMalformed();

        aggregator.MalformedEvents.Should().Be(1);
        aggregator.Snapshot().Should().BeEmpty();
    }

    private static UdpActivityEvent Event(
        DateTimeOffset time,
        int remotePort = 27015,
        int size = 64,
        UdpDirection direction = UdpDirection.Send,
        int localPort = 51000) =>
        new(
            4242,
            IPAddress.Parse("192.168.1.10"),
            localPort,
            IPAddress.Parse("203.0.113.5"),
            remotePort,
            size,
            Math.Max(0, size - 28),
            7,
            direction,
            time,
            "UdpIpSend",
            10,
            2);

    private static DateTimeOffset Time(int second) =>
        DateTimeOffset.Parse($"2026-11-23T12:00:{second:00}Z");
}
