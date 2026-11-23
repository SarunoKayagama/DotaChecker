using System.Net;
using DotaChecker.Core.Game;
using DotaChecker.Core.Models;

namespace DotaChecker.Tests.Game;

public sealed class GameEndpointClassifierTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-23T12:00:00Z");
    private readonly GameEndpointClassifier _classifier = new();

    [Fact]
    public void SingleShortUdpEndpoint_IsUnknownLow()
    {
        var record = Record(minutes: 1, observations: 1, send: 10, receive: 10);

        var result = _classifier.Classify(record);

        result.Category.Should().Be(GameEndpointCategory.Unknown);
        result.Evidence.Should().Be(EvidenceLevel.Low);
    }

    [Fact]
    public void StableBidirectionalEndpoint_IsLikelyMediumButNotHigh()
    {
        var record = Record(minutes: 2, observations: 4, send: 300, receive: 400);

        var result = _classifier.Classify(record);

        result.Category.Should().Be(GameEndpointCategory.LikelyGameRelay);
        result.Evidence.Should().Be(EvidenceLevel.Medium);
        result.Reasons.Should().Contain(reason => reason.StartsWith("Fact:", StringComparison.Ordinal));
        result.Reasons.Should().Contain(reason => reason.StartsWith("Heuristic:", StringComparison.Ordinal));
    }

    [Fact]
    public void ManyEndpointsForSamePid_DoesNotClassifyAllAsRelay()
    {
        var dominant = Record(minutes: 2, observations: 4, send: 1000, receive: 1000, remotePort: 27015);
        var weak = Enumerable.Range(1, 4)
            .Select(index => Record(minutes: 2, observations: 4, send: 1, receive: 1, remotePort: 30000 + index))
            .ToArray();
        var all = new[] { dominant }.Concat(weak).ToArray();

        _classifier.Classify(dominant, all).Category.Should().Be(GameEndpointCategory.LikelyGameRelay);
        foreach (var endpoint in weak)
        {
            var result = _classifier.Classify(endpoint, all);
            result.Category.Should().Be(GameEndpointCategory.Unknown);
            result.Evidence.Should().Be(EvidenceLevel.Low);
        }
    }

    [Fact]
    public void OneWayTraffic_IsUnknownLow()
    {
        var record = Record(minutes: 5, observations: 6, send: 500, receive: 0);

        var result = _classifier.Classify(record);

        result.Category.Should().Be(GameEndpointCategory.Unknown);
        result.Evidence.Should().Be(EvidenceLevel.Low);
        result.Reasons.Should().Contain(reason => reason.Contains("one-way", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RemotePortChange_ProducesDifferentKeys()
    {
        var first = Record(minutes: 2, observations: 4, send: 100, receive: 100, remotePort: 27015);
        var second = Record(minutes: 2, observations: 4, send: 100, receive: 100, remotePort: 27016);

        first.Key.Should().NotBe(second.Key);
        _classifier.Classify(first, new[] { first, second }).Category.Should().Be(GameEndpointCategory.LikelyGameRelay);
    }

    [Fact]
    public void RepeatedAfterGameRestart_DoesNotRaiseEvidenceToHigh()
    {
        var record = Record(minutes: 5, observations: 8, send: 800, receive: 900);
        record.ProcessStartTimes.Add(Start.AddMinutes(-10));

        var result = _classifier.Classify(record);

        result.Category.Should().Be(GameEndpointCategory.LikelyGameRelay);
        result.Evidence.Should().Be(EvidenceLevel.Medium);
        result.Reasons.Should().Contain(reason => reason.Contains("repetition alone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InsufficientData_IsUnknownLowEvenWithLargeSizeFields()
    {
        var record = Record(minutes: 1, observations: 1, send: 2, receive: 2);
        record.ObservedSizeFieldSum = 1_000_000;
        record.ObservedDataSizeFieldSum = 1_000_000;

        var result = _classifier.Classify(record);

        result.Category.Should().Be(GameEndpointCategory.Unknown);
        result.Evidence.Should().Be(EvidenceLevel.Low);
    }

    [Fact]
    public void UnknownProcess_IsUnknownLow()
    {
        var record = Record(minutes: 5, observations: 6, send: 100, receive: 100);
        record.ProcessName = "unknown.exe";

        var result = _classifier.Classify(record);

        result.Category.Should().Be(GameEndpointCategory.Unknown);
        result.Evidence.Should().Be(EvidenceLevel.Low);
    }

    [Fact]
    public void TcpEndpointWithoutUdpEvidence_IsUnknownLow()
    {
        var record = Record(minutes: 5, observations: 6, send: 100, receive: 100);
        record.Protocol = TransportProtocol.Tcp;
        record.Source = EndpointDetectionSource.Tcp;

        var result = _classifier.Classify(record);

        result.Category.Should().Be(GameEndpointCategory.Unknown);
        result.Evidence.Should().Be(EvidenceLevel.Low);
        result.Reasons.Should().Contain(reason => reason.Contains("TCP", StringComparison.Ordinal));
    }

    [Fact]
    public void ManuallyExcludedEndpoint_IsOtherLow()
    {
        var record = Record(minutes: 5, observations: 6, send: 100, receive: 100);
        record.ManuallyExcluded = true;

        _classifier.Classify(record).Category.Should().Be(GameEndpointCategory.Other);
    }

    private static GameEndpointRecord Record(
        int minutes,
        int observations,
        long send,
        long receive,
        int remotePort = 27024) =>
        new()
        {
            Key = GameEndpointKeys.Build(24568, TransportProtocol.Udp,
                IPAddress.Parse("203.0.113.9"), remotePort, IPAddress.Parse("192.168.1.10"), 65479),
            RemoteAddress = IPAddress.Parse("203.0.113.9"),
            RemotePort = remotePort,
            LocalAddress = IPAddress.Parse("192.168.1.10"),
            LocalPort = 65479,
            Protocol = TransportProtocol.Udp,
            ProcessId = 24568,
            ProcessName = "dota2",
            ProcessPath = @"C:\Games\dota2.exe",
            Source = EndpointDetectionSource.Etw,
            FirstSeen = Start,
            LastSeen = Start.AddMinutes(minutes),
            SendCount = send,
            ReceiveCount = receive,
            ObservationCount = observations,
            ProcessStartTimes = new List<DateTimeOffset> { Start.AddSeconds(-1) },
        };
}
