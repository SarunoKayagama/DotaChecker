namespace DotaChecker.Core.Game;

/// <summary>
/// Источник наблюдаемых endpoint. Реализация ETW находится в Network и не знает
/// о классификаторе или хранилище.
/// </summary>
public interface IGameEndpointSource
{
    string DisplayName { get; }

    IReadOnlyList<GameEndpointObservation> CaptureSnapshot();
}
