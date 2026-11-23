using System.Net;

namespace DotaChecker.Core.Models;

/// <summary>Снимок одного TCP-соединения или UDP-endpoint'а.</summary>
public sealed record ConnectionInfo
{
    public required TransportProtocol Protocol { get; init; }

    public required IPAddress LocalAddress { get; init; }

    public required int LocalPort { get; init; }

    public required IPAddress RemoteAddress { get; init; }

    public required int RemotePort { get; init; }

    public required int ProcessId { get; init; }

    public required string ProcessName { get; init; }

    public required string ProcessPath { get; init; }

    /// <summary>Текстовое состояние TCP; для UDP — пусто.</summary>
    public string TcpState { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>false, если Windows не вернула liCreateTimestamp.</summary>
    public bool HasCreationTime { get; init; }

    /// <summary>Стабильный ключ строки между опросами.</summary>
    public string Key =>
        $"{(int)Protocol}:{LocalAddress}:{LocalPort}:{RemoteAddress}:{RemotePort}:{ProcessId}";

    /// <summary>Удалённый адрес, пригодный для запуска диагностики.</summary>
    public IPAddress DiagnosticAddress =>
        RemoteAddress.Equals(IPAddress.Any) ? LocalAddress : RemoteAddress;

    public bool HasRemoteEndpoint => !RemoteAddress.Equals(IPAddress.Any) && RemotePort != 0;
}
