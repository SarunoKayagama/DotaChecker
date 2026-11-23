using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using DotaChecker.Core.Diagnostics;

namespace DotaChecker.Network.Icmp;

/// <summary>
/// ICMP-транспорт на базе BCL Ping (внутри использует IcmpSendEcho через iphlpapi.dll).
/// Каждый слот владеет отдельным экземпляром Ping, поэтому параллельные TTL-пробы
/// не мешают сопоставлению ответов. Права администратора не требуются.
/// </summary>
public sealed class IcmpProbeTransport : IProbeTransport
{
    private static readonly byte[] DefaultPayload = CreatePayload(32);

    private readonly ConcurrentDictionary<int, Ping> _pings = new();

    public static byte[] CreatePayload(int size)
    {
        var payload = new byte[size];
        for (var i = 0; i < size; i++)
        {
            payload[i] = (byte)(0xA5 + i);
        }

        return payload;
    }

    public async Task<ProbeReply> SendAsync(ProbeRequest request, CancellationToken cancellationToken)
    {
        var ping = _pings.GetOrAdd(request.Slot, _ => new Ping());
        var options = new PingOptions(request.Ttl, dontFragment: true);

        PingReply reply;
        try
        {
            reply = await ping.SendPingAsync(
                    request.Target,
                    request.Timeout,
                    request.Payload ?? DefaultPayload,
                    options,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PingException exception)
        {
            return new ProbeReply(request.Target, ProbeOutcome.Error, 0, exception.Message);
        }

        var outcome = reply.Status switch
        {
            IPStatus.Success => ProbeOutcome.EchoReply,
            IPStatus.TtlExpired => ProbeOutcome.TimeExceeded,
            IPStatus.DestinationUnreachable or
            IPStatus.DestinationHostUnreachable or
            IPStatus.DestinationNetworkUnreachable or
            IPStatus.DestinationProtocolUnreachable or
            IPStatus.DestinationPortUnreachable => ProbeOutcome.DestinationUnreachable,
            _ => ProbeOutcome.NoReply,
        };

        return new ProbeReply(reply.Address, outcome, reply.RoundtripTime, reply.Status.ToString());
    }

    public ValueTask DisposeAsync()
    {
        foreach (var ping in _pings.Values)
        {
            ping.Dispose();
        }

        _pings.Clear();
        return ValueTask.CompletedTask;
    }
}
