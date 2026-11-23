using System.Net;
using DotaChecker.Core.Models;

namespace DotaChecker.Core.Game;

/// <summary>
/// Наблюдаемый факт: один endpoint, привязанный к процессу.
/// Не содержит классификации и не делает выводов о назначении трафика.
/// </summary>
public sealed record GameEndpointObservation
{
    public required IPAddress RemoteAddress { get; init; }

    public required int RemotePort { get; init; }

    public IPAddress? LocalAddress { get; init; }

    public int LocalPort { get; init; }

    public required TransportProtocol Protocol { get; init; }

    public required int ProcessId { get; init; }

    public required string ProcessName { get; init; }

    public string ProcessPath { get; init; } = string.Empty;

    public EndpointDetectionSource Source { get; init; } = EndpointDetectionSource.Unknown;

    public DateTimeOffset FirstSeen { get; init; }

    public DateTimeOffset LastSeen { get; init; }

    public long SendCount { get; init; }

    public long ReceiveCount { get; init; }

    /// <summary>Число discovery/наблюдений, в которых endpoint встречался.</summary>
    public int ObservationCount { get; init; } = 1;

    /// <summary>Время запуска процесса; используется для определения перезапуска игры.</summary>
    public DateTimeOffset? ProcessStartTime { get; init; }

    public long ConnectionId { get; init; }

    /// <summary>Сумма поля `size` ETW. Семантика не подтверждена; не размер UDP payload.</summary>
    public long ObservedSizeFieldSum { get; init; }

    /// <summary>Сумма поля `dsize` ETW. Семантика не подтверждена.</summary>
    public long ObservedDataSizeFieldSum { get; init; }

    public string Key => GameEndpointKeys.Build(
        ProcessId,
        Protocol,
        RemoteAddress,
        RemotePort,
        LocalAddress,
        LocalPort);
}

public static class GameEndpointKeys
{
    public static string Build(
        int processId,
        TransportProtocol protocol,
        IPAddress remoteAddress,
        int remotePort,
        IPAddress? localAddress,
        int localPort) =>
        $"{(int)protocol}|{processId}|{remoteAddress.AddressFamily}|{remoteAddress}|{remotePort}|" +
        $"{localAddress?.ToString() ?? "-"}:{localPort}";
}
