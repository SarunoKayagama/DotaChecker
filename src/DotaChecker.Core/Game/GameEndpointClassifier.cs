using DotaChecker.Core.Models;

namespace DotaChecker.Core.Game;

/// <summary>
/// Чистый детерминированный классификатор. Не использует WinAPI, ETW, файлы и UI.
/// Не считает `size`/`dsize` подтверждёнными размерами пакетов.
/// </summary>
public sealed class GameEndpointClassifier
{
    public const string Version = "1.1";

    private const string DotaProcessName = "dota2";
    private static readonly TimeSpan MinimumActiveDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HighEvidenceDuration = TimeSpan.FromSeconds(30);

    public GameEndpointClassification Classify(
        GameEndpointRecord record,
        IReadOnlyList<GameEndpointRecord>? context = null)
    {
        var reasons = new List<string>();
        var now = DateTimeOffset.UtcNow;

        reasons.Add($"Fact: protocol={record.Protocol}, process={record.ProcessName} (pid {record.ProcessId}), " +
                    $"send={record.SendCount}, receive={record.ReceiveCount}, observations={record.ObservationCount}, " +
                    $"active={record.ActiveDuration.TotalSeconds:F1}s, sessions={record.SessionCount}");

        if (record.ManuallyExcluded)
        {
            reasons.Add("Fact: endpoint excluded manually.");
            return Result(GameEndpointCategory.Other, EvidenceLevel.Low, reasons, now);
        }

        if (!IsDotaProcess(record))
        {
            reasons.Add("Heuristic: process identity is not dota2; no game-specific evidence.");
            return Result(GameEndpointCategory.Unknown, EvidenceLevel.Low, reasons, now);
        }

        if (record.Protocol != TransportProtocol.Udp)
        {
            reasons.Add("Fact: only TCP observation is available; TCP connect is not game UDP evidence.");
            return Result(GameEndpointCategory.Unknown, EvidenceLevel.Low, reasons, now);
        }

        if (record.Source != EndpointDetectionSource.Etw && !record.ManuallyIncluded)
        {
            reasons.Add("Heuristic: endpoint did not come from UDP activity observation and was not manually included.");
            return Result(GameEndpointCategory.Unknown, EvidenceLevel.Low, reasons, now);
        }

        if (record.RemotePort <= 0)
        {
            reasons.Add("Fact: remote port is missing.");
            return Result(GameEndpointCategory.Unknown, EvidenceLevel.Low, reasons, now);
        }

        var others = (context ?? Array.Empty<GameEndpointRecord>())
            .Where(item => item.ProcessId == record.ProcessId &&
                           item.Protocol == TransportProtocol.Udp &&
                           !item.ManuallyExcluded)
            .ToArray();

        if (IsOneOfManyWeakEndpoints(record, others, reasons))
        {
            return Result(GameEndpointCategory.Unknown, EvidenceLevel.Low, reasons, now);
        }

        if (record.SendCount == 0 || record.ReceiveCount == 0)
        {
            reasons.Add("Heuristic: one-way traffic only; cannot distinguish game relay from voice or other UDP traffic.");
            return Result(GameEndpointCategory.Unknown, EvidenceLevel.Low, reasons, now);
        }

        reasons.Add("Fact: bidirectional UDP traffic from dota2.exe.");

        if (record.ObservationCount < 2 || record.ActiveDuration < MinimumActiveDuration)
        {
            reasons.Add("Heuristic: too few observations or too short activity to classify.");
            return Result(GameEndpointCategory.Unknown, EvidenceLevel.Low, reasons, now);
        }

        reasons.Add("Heuristic: endpoint persists across multiple observations.");

        // Повторяемость наблюдений и перезапуски игры не являются самостоятельным
        // доказательством роли gaming relay. Поэтому EvidenceLevel.High не ставим
        // ни при каких комбинациях этих признаков.
        var evidence = EvidenceLevel.Medium;
        if (record.SessionCount >= 2 || record.ObservationCount >= 5 || record.ActiveDuration >= HighEvidenceDuration)
        {
            reasons.Add("Fact: repeated across observations/sessions, but repetition alone is not proof of the endpoint role.");
        }

        reasons.Add("Hypothesis: likely game relay/SDR endpoint (not a confirmed game server).");
        return Result(GameEndpointCategory.LikelyGameRelay, evidence, reasons, now);
    }

    private static bool IsDotaProcess(GameEndpointRecord record) =>
        string.Equals(record.ProcessName, DotaProcessName, StringComparison.OrdinalIgnoreCase);

    private static bool IsOneOfManyWeakEndpoints(
        GameEndpointRecord record,
        IReadOnlyList<GameEndpointRecord> sameProcess,
        List<string> reasons)
    {
        if (sameProcess.Count <= 3)
        {
            return false;
        }

        var total = sameProcess.Sum(item => item.SendCount + item.ReceiveCount);
        if (total <= 0)
        {
            return false;
        }

        var share = (double)(record.SendCount + record.ReceiveCount) / total;
        if (share >= 0.5)
        {
            return false;
        }

        reasons.Add("Heuristic: many UDP endpoints for the same PID and this one is not dominant; " +
                    "not classifying it as game relay automatically.");
        return true;
    }

    private GameEndpointClassification Result(
        GameEndpointCategory category,
        EvidenceLevel evidence,
        IReadOnlyList<string> reasons,
        DateTimeOffset now) =>
        new(category, evidence, reasons, now, Version);
}
