using System.Net;
using DotaChecker.Core.Models;

namespace DotaChecker.Core.Game;

/// <summary>
/// Агрегированные факты по одному endpoint. Классификация хранится отдельно,
/// чтобы при изменении правил её можно было пересчитать без потери наблюдений.
/// </summary>
public sealed class GameEndpointRecord
{
    public string Key { get; set; } = string.Empty;

    public IPAddress RemoteAddress { get; set; } = IPAddress.None;

    public int RemotePort { get; set; }

    public IPAddress? LocalAddress { get; set; }

    public int LocalPort { get; set; }

    public TransportProtocol Protocol { get; set; } = TransportProtocol.Udp;

    public int ProcessId { get; set; }

    public string ProcessName { get; set; } = string.Empty;

    public string ProcessPath { get; set; } = string.Empty;

    public EndpointDetectionSource Source { get; set; } = EndpointDetectionSource.Unknown;

    public DateTimeOffset FirstSeen { get; set; }

    public DateTimeOffset LastSeen { get; set; }

    public long SendCount { get; set; }

    public long ReceiveCount { get; set; }

    public int ObservationCount { get; set; }

    /// <summary>Различные времена запуска процесса (перезапуски игры).</summary>
    public List<DateTimeOffset> ProcessStartTimes { get; set; } = new();

    /// <summary>Число различных времён запуска процесса.</summary>
    public int SessionCount
    {
        get => Math.Max(1, ProcessStartTimes.Count);
        set { }
    }

    public long ConnectionId { get; set; }

    public long ObservedSizeFieldSum { get; set; }

    public long ObservedDataSizeFieldSum { get; set; }

    public GameEndpointClassification? Classification { get; set; }

    /// <summary>История классификаций (последние N), чтобы пересчёт правил был проверяем.</summary>
    public List<GameEndpointClassification> ClassificationHistory { get; set; } = new();

    /// <summary>Последние времена обнаружений (ограниченный список).</summary>
    public List<DateTimeOffset> ObservationTimes { get; set; } = new();

    public bool ManuallyExcluded { get; set; }

    public bool ManuallyIncluded { get; set; }

    public bool IsBidirectional => SendCount > 0 && ReceiveCount > 0;

    public TimeSpan ActiveDuration => LastSeen >= FirstSeen ? LastSeen - FirstSeen : TimeSpan.Zero;
}
