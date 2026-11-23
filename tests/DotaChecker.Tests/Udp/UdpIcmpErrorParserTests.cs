using System.Net;
using DotaChecker.Network.Udp;

namespace DotaChecker.Tests.Udp;

public sealed class UdpIcmpErrorParserTests
{
    [Fact]
    public void TimeExceeded_QuotesOriginalUdpPorts()
    {
        var packet = Build(11, 0, 50000, 33434);

        var parsed = UdpIcmpErrorParser.Parse(packet, IPAddress.Parse("192.0.2.1"), 12.5);

        parsed.Should().NotBeNull();
        parsed!.Kind.Should().Be(IcmpErrorKind.TimeExceeded);
        parsed.QuotedSourcePort.Should().Be(50000);
        parsed.QuotedDestinationPort.Should().Be(33434);
        parsed.RttMs.Should().Be(12.5);
    }

    [Fact]
    public void DestinationUnreachable_IsParsed()
    {
        var packet = Build(3, 3, 51000, 443);

        var parsed = UdpIcmpErrorParser.Parse(packet, IPAddress.Parse("192.0.2.2"), 5);

        parsed!.Kind.Should().Be(IcmpErrorKind.DestinationUnreachable);
        parsed.QuotedDestinationPort.Should().Be(443);
    }

    [Fact]
    public void MalformedPackets_AreRejected()
    {
        UdpIcmpErrorParser.Parse(new byte[10], IPAddress.Any, 0).Should().BeNull();
        UdpIcmpErrorParser.Parse(new byte[28], IPAddress.Any, 0).Should().BeNull();
    }

    private static byte[] Build(byte icmpType, byte icmpCode, int sourcePort, int destinationPort)
    {
        var packet = new byte[20 + 8 + 20 + 8];
        packet[0] = 0x45;
        packet[20] = icmpType;
        packet[21] = icmpCode;
        packet[28] = 0x45;
        packet[28 + 9] = 17; // quoted protocol UDP
        var udp = 48;
        packet[udp] = (byte)(sourcePort >> 8);
        packet[udp + 1] = (byte)sourcePort;
        packet[udp + 2] = (byte)(destinationPort >> 8);
        packet[udp + 3] = (byte)destinationPort;
        packet[udp + 4] = 0;
        packet[udp + 5] = 8;
        return packet;
    }
}
