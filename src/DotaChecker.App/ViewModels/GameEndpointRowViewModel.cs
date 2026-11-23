using CommunityToolkit.Mvvm.ComponentModel;
using DotaChecker.Core.Game;
using DotaChecker.Core.Models;

namespace DotaChecker.App.ViewModels;

public sealed class GameEndpointRowViewModel : ObservableObject
{
    public GameEndpointRowViewModel(GameEndpointRecord record) => Update(record);

    public string Key { get; private set; } = string.Empty;

    public string RemoteIp { get; private set; } = string.Empty;

    public int RemotePort { get; private set; }

    public string RemoteEndPoint => $"{RemoteIp}:{RemotePort}";

    public string LocalEndPoint { get; private set; } = "—";

    public int ProcessId { get; private set; }

    public string ProcessName { get; private set; } = string.Empty;

    public string Protocol { get; private set; } = string.Empty;

    public GameEndpointCategory Category { get; private set; }

    public EvidenceLevel Evidence { get; private set; }

    public string CategoryText => Category switch
    {
        GameEndpointCategory.LikelyGameRelay => "LikelyGameRelay (гипотеза)",
        GameEndpointCategory.SteamCoordinator => "SteamCoordinator",
        GameEndpointCategory.Voice => "Voice",
        GameEndpointCategory.Other => "Other",
        _ => "Unknown",
    };

    public string EvidenceText => Evidence switch
    {
        EvidenceLevel.High => "High (не гарантия)",
        EvidenceLevel.Medium => "Medium",
        _ => "Low",
    };

    public string Reasons { get; private set; } = string.Empty;

    public DateTimeOffset FirstSeen { get; private set; }

    public DateTimeOffset LastSeen { get; private set; }

    public long SendCount { get; private set; }

    public long ReceiveCount { get; private set; }

    public string ManualState { get; private set; } = string.Empty;

    public string Details { get; private set; } = string.Empty;

    public string MonitoringText { get; private set; } = "не проверялся";

    public void SetMonitoring(EndpointMonitoringSummary? summary)
    {
        MonitoringText = summary is null
            ? "не проверялся"
            : $"{summary.StatusText}; ICMP нет ответа {summary.ResponseLossPct:F1}%, " +
              $"avg {summary.AverageRttMs:F0} мс, n={summary.Sent}";
        OnPropertyChanged(nameof(MonitoringText));
    }

    public void Update(GameEndpointRecord record)
    {
        Key = record.Key;
        RemoteIp = record.RemoteAddress.ToString();
        RemotePort = record.RemotePort;
        LocalEndPoint = record.LocalAddress is null
            ? $"—:{record.LocalPort}"
            : $"{record.LocalAddress}:{record.LocalPort}";
        ProcessId = record.ProcessId;
        ProcessName = record.ProcessName;
        Protocol = record.Protocol.ToString();
        Category = record.Classification?.Category ?? GameEndpointCategory.Unknown;
        Evidence = record.Classification?.Evidence ?? EvidenceLevel.Low;
        Reasons = record.Classification is null
            ? "Классификация ещё не выполнена."
            : string.Join(Environment.NewLine, record.Classification.Reasons);
        FirstSeen = record.FirstSeen;
        LastSeen = record.LastSeen;
        SendCount = record.SendCount;
        ReceiveCount = record.ReceiveCount;
        ManualState = record.ManuallyExcluded
            ? "Исключён вручную"
            : record.ManuallyIncluded ? "Включён вручную" : "Авто";

        var observations = record.ObservationTimes.Count == 0
            ? "—"
            : string.Join(", ", record.ObservationTimes
                .OrderBy(value => value)
                .TakeLast(10)
                .Select(value => value.LocalDateTime.ToString("HH:mm:ss")));
        var classifications = record.ClassificationHistory.Count == 0
            ? "—"
            : string.Join(Environment.NewLine, record.ClassificationHistory
                .Select(item => $"{item.ClassifiedAt.LocalDateTime:HH:mm:ss} {item.Category}/{item.Evidence} ({item.ClassifierVersion})"));

        Details = string.Join(Environment.NewLine,
            $"Remote: {RemoteEndPoint}",
            $"Local: {LocalEndPoint}",
            $"Process: {ProcessName} ({ProcessId}), {record.ProcessPath}",
            $"Source: {record.Source}, protocol: {Protocol}",
            $"Observations: {record.ObservationCount}, sessions: {record.SessionCount}",
            $"Send/Receive: {SendCount}/{ReceiveCount}",
            $"First/Last: {FirstSeen.LocalDateTime:G} / {LastSeen.LocalDateTime:G}",
            $"Observed size field sum: {record.ObservedSizeFieldSum} (семантика не подтверждена)",
            $"Observed dsize field sum: {record.ObservedDataSizeFieldSum} (семантика не подтверждена)",
            string.Empty,
            "История обнаружений: " + observations,
            string.Empty,
            "История классификаций:",
            classifications,
            string.Empty,
            "Причины текущей классификации:",
            Reasons);

        OnPropertyChanged(string.Empty);
    }
}
