using DotaChecker.Core.Optimization;

namespace DotaChecker.Tests.Optimization;

public sealed class RouteOptimizerTests
{
    private readonly RouteOptimizer _optimizer = new();

    [Fact]
    public void DifferentEndpoints_AreInvalid()
    {
        var direct = Measurement("direct", "key-1", interfaceIndex: 1);
        var warp = Measurement("warp", "key-2", interfaceIndex: 2);

        _optimizer.Validate(direct, warp).IsValid.Should().BeFalse();
        _optimizer.Compare(direct, warp).Status.Should().Be(RouteComparisonStatus.RouteComparisonInvalid);
    }

    [Fact]
    public void UnverifiedInterface_IsInvalid()
    {
        var direct = Measurement("direct", "key", interfaceIndex: 1);
        var warp = Measurement("warp", "key", interfaceIndex: 2, verified: false);

        _optimizer.Compare(direct, warp).Status.Should().Be(RouteComparisonStatus.RouteComparisonInvalid);
    }

    [Fact]
    public void SameInterface_IsInvalid()
    {
        var direct = Measurement("direct", "key", interfaceIndex: 1);
        var warp = Measurement("warp", "key", interfaceIndex: 1);

        _optimizer.Compare(direct, warp).Status.Should().Be(RouteComparisonStatus.RouteComparisonInvalid);
    }

    [Fact]
    public void InsufficientSamples_IsInvalid()
    {
        var direct = Measurement("direct", "key", interfaceIndex: 1, samples: 5);
        var warp = Measurement("warp", "key", interfaceIndex: 2, samples: 5);

        _optimizer.Compare(direct, warp).Status.Should().Be(RouteComparisonStatus.RouteComparisonInvalid);
    }

    [Fact]
    public void IdenticalConditions_ProduceNoSignificantDifference()
    {
        var direct = Measurement("direct", "key", interfaceIndex: 1);
        var warp = Measurement("warp", "key", interfaceIndex: 2);

        var result = _optimizer.Compare(direct, warp);

        result.Status.Should().Be(RouteComparisonStatus.NoSignificantDifference);
        result.Winner.Should().BeNull();
        result.Validation.IsValid.Should().BeTrue();
    }

    [Fact]
    public void LargeLossDifference_DecidesByDelivery()
    {
        var direct = Measurement("direct", "key", interfaceIndex: 1, received: 100);
        var warp = Measurement("warp", "key", interfaceIndex: 2, received: 30);

        var result = _optimizer.Compare(direct, warp);

        result.Status.Should().Be(RouteComparisonStatus.Better);
        result.Winner!.Name.Should().Be("direct");
    }

    [Fact]
    public void JitterDifference_DecidesAfterEqualDelivery()
    {
        var direct = Measurement("direct", "key", interfaceIndex: 1, jitter: 20);
        var warp = Measurement("warp", "key", interfaceIndex: 2, jitter: 2);

        var result = _optimizer.Compare(direct, warp);

        result.Status.Should().Be(RouteComparisonStatus.Worse);
        result.Winner!.Name.Should().Be("warp");
    }

    [Fact]
    public void RttDifference_IsLastCriterion()
    {
        var direct = Measurement("direct", "key", interfaceIndex: 1, rtt: 30);
        var warp = Measurement("warp", "key", interfaceIndex: 2, rtt: 10);

        var result = _optimizer.Compare(direct, warp);

        result.Status.Should().Be(RouteComparisonStatus.Worse);
        result.Winner!.Name.Should().Be("warp");
    }

    [Fact]
    public void ServiceInvalidatesPreviousComparisonOnNewMeasurement()
    {
        var service = new RouteComparisonService();
        service.SetMeasurement(RouteCandidateKind.Direct, Measurement("direct", "key", 1));
        service.SetMeasurement(RouteCandidateKind.Warp, Measurement("warp", "key", 2));
        service.Compare().Status.Should().Be(RouteComparisonStatus.NoSignificantDifference);

        service.SetMeasurement(RouteCandidateKind.Warp, Measurement("warp", "key", 2, received: 30));

        service.LastComparison!.Status.Should().Be(RouteComparisonStatus.Testing);
        service.Compare().Status.Should().Be(RouteComparisonStatus.Better);
    }

    private static RouteMeasurement Measurement(
        string name,
        string endpointKey,
        int interfaceIndex,
        bool verified = true,
        int samples = 100,
        int received = 100,
        double jitter = 5,
        double rtt = 20) =>
        new(
            new RouteCandidate(
                name == "direct" ? RouteCandidateKind.Direct : RouteCandidateKind.Warp,
                name,
                endpointKey,
                "203.0.113.9",
                interfaceIndex,
                $"if{interfaceIndex}",
                string.Empty,
                name == "direct" ? "direct" : "running",
                verified,
                true),
            "ICMP",
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(samples),
            samples,
            received,
            Enumerable.Repeat(rtt, Math.Max(1, samples)).ToArray(),
            samples == 0 ? 0 : 100.0 * (samples - received) / samples,
            jitter,
            0);
}
