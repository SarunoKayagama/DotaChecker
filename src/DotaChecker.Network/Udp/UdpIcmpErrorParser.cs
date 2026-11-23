using System.Net;

namespace DotaChecker.Network.Udp;

public enum IcmpErrorKind
{
    None,
    TimeExceeded,
    DestinationUnreachable,
    Other,
}

/// <summary>Разбор ICMP-ошибки, цитирующей исходный UDP-датаграм.</summary>
public sealed record UdpIcmpError(
    IcmpErrorKind Kind,
    IPAddress Responder,
    int QuotedSourcePort,
    int QuotedDestinationPort,
    double RttMs);

/// <summary>
/// Чистый парсер ICMP Type 11/Code 0 и Type 3/Code 3.
/// ICMP цитирует IP-заголовок + первые 8 байт датаграммы: для UDP это сам UDP-заголовок,
/// поэтому совпадать можно только по портам, не по полезной нагрузке.
/// </summary>
public static class UdpIcmpErrorParser
{
    public static UdpIcmpError? Parse(byte[] packet, IPAddress responder, double rttMs)
    {
        if (packet.Length < 28)
        {
            return null;
        }

        var ipHeaderLength = (packet[0] & 0x0F) * 4;
        if (ipHeaderLength < 20 || packet.Length < ipHeaderLength + 8)
        {
            return null;
        }

        var icmpOffset = ipHeaderLength;
        var kind = packet[icmpOffset] switch
        {
            11 when packet[icmpOffset + 1] == 0 => IcmpErrorKind.TimeExceeded,
            3 => IcmpErrorKind.DestinationUnreachable,
            _ => IcmpErrorKind.Other,
        };
        if (kind == IcmpErrorKind.Other)
        {
            return null;
        }

        var quotedIpOffset = icmpOffset + 8;
        if (packet.Length < quotedIpOffset + 20)
        {
            return null;
        }

        var quotedIpHeaderLength = (packet[quotedIpOffset] & 0x0F) * 4;
        var quotedUdpOffset = quotedIpOffset + quotedIpHeaderLength;
        if (quotedIpHeaderLength < 20 || packet.Length < quotedUdpOffset + 8)
        {
            return null;
        }

        var sourcePort = (packet[quotedUdpOffset] << 8) | packet[quotedUdpOffset + 1];
        var destinationPort = (packet[quotedUdpOffset + 2] << 8) | packet[quotedUdpOffset + 3];

        return new UdpIcmpError(kind, responder, sourcePort, destinationPort, rttMs);
    }
}
