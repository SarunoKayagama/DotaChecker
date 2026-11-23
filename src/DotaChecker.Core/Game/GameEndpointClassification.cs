namespace DotaChecker.Core.Game;

/// <summary>Результат классификатора. Хранится отдельно от фактов наблюдений.</summary>
public sealed record GameEndpointClassification(
    GameEndpointCategory Category,
    EvidenceLevel Evidence,
    IReadOnlyList<string> Reasons,
    DateTimeOffset ClassifiedAt,
    string ClassifierVersion);
