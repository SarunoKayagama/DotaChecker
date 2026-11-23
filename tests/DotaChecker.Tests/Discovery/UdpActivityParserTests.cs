using System.Net;
using DotaChecker.Network.Discovery;
using DotaChecker.Network.Discovery.Models;

namespace DotaChecker.Tests.Discovery;

public sealed class UdpActivityParserTests
{
    private static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-11-23T12:00:00Z");

    [Fact]
    public void SendEvent_UsesSourceAsLocalAndDestinationAsRemote()
    {
        var payload = Payload(pid: 4242, source: 0x0100007Fu, sourcePort: 51000,
            destination: 0x0200007Fu, destinationPort: 27015, size: 64, connectionId: 99);

        var parsed = UdpActivityEventParser.TryParse("UdpIpSend", 10, 2, payload, Time, out var result);

        parsed.Should().BeTrue();
        result.Direction.Should().Be(UdpDirection.Send);
        result.ProcessId.Should().Be(4242);
        result.LocalAddress.Should().Be(IPAddress.Parse("127.0.0.1"));
        result.LocalPort.Should().Be(51000);
        result.RemoteAddress.Should().Be(IPAddress.Parse("127.0.0.2"));
        result.RemotePort.Should().Be(27015);
        result.SizeBytes.Should().Be(64);
        result.DataSizeBytes.Should().Be(36);
        result.ConnectionId.Should().Be(99);
    }

    [Fact]
    public void ReceiveEvent_SwapsAddressRoles()
    {
        var payload = Payload(pid: 4242, source: 0x0200007Fu, sourcePort: 27015,
            destination: 0x0100007Fu, destinationPort: 51000, size: 32, connectionId: 99);

        var parsed = UdpActivityEventParser.TryParse("UdpIpRecv", 11, 2, payload, Time, out var result);

        parsed.Should().BeTrue();
        result.Direction.Should().Be(UdpDirection.Receive);
        result.RemoteAddress.Should().Be(IPAddress.Parse("127.0.0.2"));
        result.RemotePort.Should().Be(27015);
        result.LocalAddress.Should().Be(IPAddress.Parse("127.0.0.1"));
    }

    [Fact]
    public void Ipv6ByteAddresses_AreNormalized()
    {
        var source = IPAddress.IPv6Loopback.GetAddressBytes();
        var destination = IPAddress.Parse("2001:db8::1").GetAddressBytes();
        var payload = Payload(pid: 4242, source: source, sourcePort: 51000,
            destination: destination, destinationPort: 443, size: 128, connectionId: 7);

        var parsed = UdpActivityEventParser.TryParse("UdpIpSend", 26, 2, payload, Time, out var result);

        parsed.Should().BeTrue();
        result.LocalAddress.Should().Be(IPAddress.IPv6Loopback);
        result.RemoteAddress.Should().Be(IPAddress.Parse("2001:db8::1"));
    }

    [Fact]
    public void MissingPidOrInvalidPort_IsRejected()
    {
        var withoutPid = Payload(pid: 0, source: 0x0100007Fu, sourcePort: 51000,
            destination: 0x0200007Fu, destinationPort: 27015, size: 10, connectionId: 1);
        UdpActivityEventParser.TryParse("UdpIpSend", 10, 2, withoutPid, Time, out _).Should().BeFalse();

        var invalidPort = Payload(pid: 4242, source: 0x0100007Fu, sourcePort: 70000,
            destination: 0x0200007Fu, destinationPort: 27015, size: 10, connectionId: 1);
        UdpActivityEventParser.TryParse("UdpIpSend", 10, 2, invalidPort, Time, out _).Should().BeFalse();
    }

    [Fact]
    public void Direction_IsResolvedFromEventId()
    {
        UdpActivityEventParser.ResolveDirection("unknown", 10).Should().Be(UdpDirection.Send);
        UdpActivityEventParser.ResolveDirection("unknown", 27).Should().Be(UdpDirection.Receive);
        UdpActivityEventParser.ResolveDirection("unknown", 99).Should().Be(UdpDirection.Unknown);
    }

    [Fact]
    public void RealManifestEventId65535_UsesEventNameForDirection()
    {
        var payload = Payload(pid: 24568, source: 0x0100007Fu, sourcePort: 51000,
            destination: 0x0200007Fu, destinationPort: 27024, size: 96, connectionId: 5);

        UdpActivityEventParser.ResolveDirection("UdpIp/Send", 65535).Should().Be(UdpDirection.Send);
        UdpActivityEventParser.ResolveDirection("UdpIp/Recv", 65535).Should().Be(UdpDirection.Receive);
        UdpActivityEventParser.ResolveDirection("unknown", 65535).Should().Be(UdpDirection.Unknown);

        UdpActivityEventParser.TryParse("UdpIp/Send", 65535, 2, payload, Time, out var parsed).Should().BeTrue();
        parsed.ProcessId.Should().Be(24568);
        parsed.RemotePort.Should().Be(27024);
    }

    [Fact]
    public void HeaderPidFallback_IsUsedWhenPayloadHasNoPid()
    {
        var payload = Payload(pid: 0, source: 0x0100007Fu, sourcePort: 51000,
            destination: 0x0200007Fu, destinationPort: 27024, size: 96, connectionId: 5);
        payload.Remove("PID");
        payload["HeaderProcessId"] = 24568;

        UdpActivityEventParser.TryParse("UdpIp/Send", 65535, 2, payload, Time, out var parsed).Should().BeTrue();
        parsed.ProcessId.Should().Be(24568);
    }

    [Fact]
    public void UnknownEventType_IsRejected()
    {
        var payload = Payload(pid: 24568, source: 0x0100007Fu, sourcePort: 51000,
            destination: 0x0200007Fu, destinationPort: 27024, size: 96, connectionId: 5);

        UdpActivityEventParser.TryParse("UdpIp/Unknown", 65535, 2, payload, Time, out _).Should().BeFalse();
    }

    private static Dictionary<string, object?> Payload(
        int pid,
        object source,
        int sourcePort,
        object destination,
        int destinationPort,
        int size,
        long connectionId) => new(StringComparer.Ordinal)
    {
        ["PID"] = pid,
        ["saddr"] = source,
        ["sport"] = sourcePort,
        ["daddr"] = destination,
        ["dport"] = destinationPort,
        ["size"] = size,
        ["dsize"] = Math.Max(0, size - 28),
        ["connid"] = connectionId,
    };
}
