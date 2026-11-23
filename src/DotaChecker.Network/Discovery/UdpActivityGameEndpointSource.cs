using System.Net;
using DotaChecker.Core.Game;
using DotaChecker.Core.Models;

namespace DotaChecker.Network.Discovery;

/// <summary>
/// Адаптер ETW-прототипа к интерфейсу источника наблюдений. Не классифицирует
/// и не сохраняет ничего — только превращает агрегаты в факты.
/// </summary>
public sealed class UdpActivityGameEndpointSource : IGameEndpointSource
{
    private readonly UdpActivityProbe _probe;
    private readonly Func<int, string> _processNameResolver;
    private readonly Func<int, DateTimeOffset?>? _processStartResolver;

    public UdpActivityGameEndpointSource(
        UdpActivityProbe probe,
        Func<int, string>? processNameResolver = null,
        Func<int, DateTimeOffset?>? processStartResolver = null)
    {
        _probe = probe;
        _processNameResolver = processNameResolver ?? (pid => $"<pid {pid}>");
        _processStartResolver = processStartResolver;
    }

    public string DisplayName => "ETW Microsoft-Windows-Kernel-Network";

    public IReadOnlyList<GameEndpointObservation> CaptureSnapshot() =>
        _probe.Snapshot().Select(item =>
        {
            var (localAddress, localPort) = ParseEndPoint(item.LocalEndPoint);
            var processName = _processNameResolver(item.ProcessId);

            return new GameEndpointObservation
            {
                RemoteAddress = item.RemoteAddress,
                RemotePort = item.RemotePort,
                LocalAddress = localAddress,
                LocalPort = localPort,
                Protocol = TransportProtocol.Udp,
                ProcessId = item.ProcessId,
                ProcessName = processName,
                Source = EndpointDetectionSource.Etw,
                FirstSeen = item.FirstSeen,
                LastSeen = item.LastSeen,
                SendCount = item.SendCount,
                ReceiveCount = item.ReceiveCount,
                ObservationCount = 1,
                ProcessStartTime = _processStartResolver?.Invoke(item.ProcessId),
                ConnectionId = item.ConnectionId,
                ObservedSizeFieldSum = item.BytesObserved,
                ObservedDataSizeFieldSum = item.DataBytesObserved,
            };
        }).ToArray();

    private static (IPAddress? Address, int Port) ParseEndPoint(string text)
    {
        var index = text.LastIndexOf(':');
        if (index <= 0)
        {
            return (null, 0);
        }

        var addressText = text[..index];
        var portText = text[(index + 1)..];
        return (
            IPAddress.TryParse(addressText, out var address) ? address : null,
            int.TryParse(portText, out var port) ? port : 0);
    }
}
