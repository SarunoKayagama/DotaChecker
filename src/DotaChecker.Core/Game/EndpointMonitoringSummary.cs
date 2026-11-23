namespace DotaChecker.Core.Game;

/// <summary>Сводка ICMP-мониторинга конкретного endpoint (не UDP-измерение).</summary>
public sealed record EndpointMonitoringSummary(
    string EndpointKey,
    string RemoteAddress,
    long Sent,
    long Received,
    double ResponseLossPct,
    double AverageRttMs,
    double JitterMs,
    double LastRttMs,
    double[] RttSamples,
    DateTimeOffset UpdatedAt,
    string StatusText);
