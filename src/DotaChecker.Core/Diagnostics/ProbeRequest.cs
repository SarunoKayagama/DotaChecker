using System.Net;

namespace DotaChecker.Core.Diagnostics;

/// <summary>Параметры одной пробы.</summary>
/// <param name="Slot">Индекс независимого слота отправки (свой Ping/сокет).</param>
/// <param name="Target">Адрес назначения, до которого идёт проба.</param>
/// <param name="Ttl">TTL; для промежуточного узла равен номеру узла.</param>
/// <param name="Timeout">Таймаут ожидания ответа.</param>
/// <param name="Payload">Полезная нагрузка или null для значения по умолчанию.</param>
public sealed record ProbeRequest(
    int Slot,
    IPAddress Target,
    int Ttl,
    TimeSpan Timeout,
    byte[]? Payload = null);
