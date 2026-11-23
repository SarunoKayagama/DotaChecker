using System.Net;
using DotaChecker.Core.Models;

namespace DotaChecker.Tests;

public sealed class RouteComparerTests
{
    private static readonly IPAddress Target = IPAddress.Parse("198.51.100.1");

    [Fact]
    public void SameRoute_HasNoChanges()
    {
        var route = Route("192.0.2.1");

        RouteComparer.Compare(route, route).HasChanges.Should().BeFalse();
    }

    [Fact]
    public void PartialIntersection_StillCountsAsStructuralChange()
    {
        var oldRoute = Route("192.0.2.1");
        var newRoute = new RouteSnapshot(
            Target,
            new[]
            {
                new RouteHop(1, new[] { IPAddress.Parse("192.0.2.1"), IPAddress.Parse("192.0.2.2") }, false),
                new RouteHop(2, new[] { Target }, true),
            },
            DateTimeOffset.UtcNow);

        var comparison = RouteComparer.Compare(oldRoute, newRoute);

        comparison.HasChanges.Should().BeTrue();
        comparison.HasStructuralChange.Should().BeTrue();
    }

    [Fact]
    public void DifferentRouter_IsStructuralChange()
    {
        var comparison = RouteComparer.Compare(Route("192.0.2.1"), Route("192.0.2.200"));

        comparison.HasStructuralChange.Should().BeTrue();
        comparison.Description.Should().Contain("hop 1");
    }

    [Fact]
    public void StabilityTracker_TreatsEcmpFlappingAsVariation()
    {
        var tracker = new RouteStabilityTracker(window: 3, minSeen: 2, confirm: 3);
        var statuses = new List<RouteStabilityStatus>
        {
            tracker.OnDiscovery(RouteWith("192.0.2.1")).Status,
            tracker.OnDiscovery(RouteWith("192.0.2.1", "192.0.2.2")).Status,
            tracker.OnDiscovery(RouteWith("192.0.2.1")).Status,
            tracker.OnDiscovery(RouteWith("192.0.2.2")).Status,
            tracker.OnDiscovery(RouteWith("192.0.2.1", "192.0.2.2")).Status,
            tracker.OnDiscovery(RouteWith("192.0.2.1")).Status,
            tracker.OnDiscovery(RouteWith("192.0.2.1", "192.0.2.2")).Status,
        };

        statuses.Should().NotContain(RouteStabilityStatus.Committed);
        tracker.CurrentFingerprint!.StableAddressesPerHop[0].Should().Contain("192.0.2.1");
    }

    [Fact]
    public void StabilityTracker_MeasuresDetectionLatencyOfRealChange()
    {
        var tracker = new RouteStabilityTracker(window: 3, minSeen: 2, confirm: 3);
        for (var i = 0; i < 5; i++)
        {
            tracker.OnDiscovery(RouteWith("192.0.2.1"));
        }

        RouteStabilityResult? committed = null;
        var discoveriesAfterChange = 0;
        for (var i = 0; i < 10 && committed is null; i++)
        {
            discoveriesAfterChange++;
            var result = tracker.OnDiscovery(RouteWith("192.0.2.99"));
            if (result.Status == RouteStabilityStatus.Committed)
            {
                committed = result;
            }
        }

        committed.Should().NotBeNull();
        committed!.CommittedLatencyDiscoveries.Should().Be(3);
        discoveriesAfterChange.Should().BeInRange(3, 6);
        tracker.CommittedLatencyDiscoveries.Should().Be(3);
    }

    private static RouteSnapshot Route(string firstHop) =>
        new(
            Target,
            new[]
            {
                new RouteHop(1, new[] { IPAddress.Parse(firstHop) }, false),
                new RouteHop(2, new[] { Target }, true),
            },
            DateTimeOffset.UtcNow);

    private static RouteSnapshot RouteWith(params string[] firstHopAddresses) =>
        new(
            Target,
            new[]
            {
                new RouteHop(1, firstHopAddresses.Select(IPAddress.Parse).ToArray(), false),
                new RouteHop(2, new[] { Target }, true),
            },
            DateTimeOffset.UtcNow);
}
