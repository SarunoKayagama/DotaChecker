using System.Net;
using System.Net.NetworkInformation;
using DotaChecker.Core.Diagnostics;
using DotaChecker.Network.Icmp;

namespace DotaChecker.Tests.Icmp;

/// <summary>
/// Проверка реального поведения .NET Ping на Windows. Запускается вручную:
/// dotnet test --filter Category=Integration
/// </summary>
[Trait("Category", "Integration")]
public sealed class RealNetworkIcmpTests
{
    private static readonly IPAddress Target = IPAddress.Parse("8.8.8.8");

    [Fact]
    public async Task EchoReplyIsReceived()
    {
        await using var transport = new IcmpProbeTransport();

        // Реальная сеть нестабильна: один таймаут не означает, что адрес недоступен.
        ProbeReply? reply = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            reply = await transport.SendAsync(
                new ProbeRequest(1, Target, 64, TimeSpan.FromSeconds(2)),
                CancellationToken.None);
            if (reply.Outcome == ProbeOutcome.EchoReply)
            {
                break;
            }
        }

        reply!.Outcome.Should().Be(ProbeOutcome.EchoReply);
        reply.Status.Should().Be(nameof(IPStatus.Success));
        reply.RttMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task TtlProbesReturnTimeExceededFromRouters()
    {
        await using var transport = new IcmpProbeTransport();

        var first = await transport.SendAsync(
            new ProbeRequest(1, Target, 1, TimeSpan.FromSeconds(2)), CancellationToken.None);
        var second = await transport.SendAsync(
            new ProbeRequest(2, Target, 2, TimeSpan.FromSeconds(2)), CancellationToken.None);

        var exceeded = new[] { first, second }.Where(reply => reply.Outcome == ProbeOutcome.TimeExceeded).ToArray();
        exceeded.Should().NotBeEmpty("хотя бы один маршрутизатор должен вернуть Time Exceeded");
        exceeded.Should().OnlyContain(reply => !reply.Responder.Equals(Target));
    }

    [Fact]
    public async Task ParallelTtlProbesRemainIndependent()
    {
        await using var transport = new IcmpProbeTransport();

        var tasks = Enumerable.Range(1, 8)
            .Select(ttl => transport.SendAsync(
                new ProbeRequest(ttl, Target, ttl, TimeSpan.FromSeconds(2)), CancellationToken.None))
            .ToArray();

        var replies = await Task.WhenAll(tasks);

        replies.Should().HaveCount(8);
        replies.Should().OnlyContain(reply => reply.RttMs >= 0);
        var firstHopReplies = await transport.SendAsync(
            new ProbeRequest(1, Target, 1, TimeSpan.FromSeconds(2)), CancellationToken.None);
        var firstHopFromRound = replies[0];
        if (firstHopFromRound.Outcome == ProbeOutcome.TimeExceeded &&
            firstHopReplies.Outcome == ProbeOutcome.TimeExceeded)
        {
            firstHopReplies.Responder.Should().Be(firstHopFromRound.Responder);
        }
    }
}
