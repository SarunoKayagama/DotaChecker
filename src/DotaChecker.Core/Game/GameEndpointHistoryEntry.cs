namespace DotaChecker.Core.Game;

/// <summary>Персистентная запись истории: факты + последняя классификация.</summary>
public sealed record GameEndpointHistoryEntry(
    GameEndpointRecord Record,
    GameEndpointClassification? Classification,
    DateTimeOffset StoredAt);
