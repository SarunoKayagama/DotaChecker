using System.Net;

namespace DotaChecker.Network.Discovery.Models;

/// <summary>Явное состояние ETW-модуля. Не подменять «работает» при недоступности.</summary>
public enum EtAvailability
{
    Unknown,
    Available,
    Unavailable,
    PermissionDenied,
    ProviderUnavailable,
    Error,
}

/// <summary>Направление UDP-события.</summary>
public enum UdpDirection
{
    Unknown,
    Send,
    Receive,
}

/// <summary>
/// Одно нормализованное UDP-событие ETW (без полезной нагрузки пакета).
/// SizeBytes — значение поля `size`; DataSizeBytes — значение поля `dsize`.
/// Семантика обоих полей должна подтверждаться реальным прогоном: не считать
/// `size` размером UDP payload без подтверждения.
/// </summary>
public sealed record UdpActivityEvent(
    int ProcessId,
    IPAddress LocalAddress,
    int LocalPort,
    IPAddress RemoteAddress,
    int RemotePort,
    int SizeBytes,
    int DataSizeBytes,
    long ConnectionId,
    UdpDirection Direction,
    DateTimeOffset Timestamp,
    string EventName,
    int EventId,
    int EventVersion);

/// <summary>Агрегат по PID + удалённый endpoint (+ локальный endpoint).</summary>
public sealed record UdpActivityAggregate(
    string Key,
    int ProcessId,
    string ProcessName,
    IPAddress RemoteAddress,
    int RemotePort,
    string LocalEndPoint,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    long EventCount,
    long SendCount,
    long ReceiveCount,
    long BytesObserved,
    long DataBytesObserved,
    long ConnectionId);
