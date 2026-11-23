namespace DotaChecker.Core.Game;

/// <summary>Источник наблюдения endpoint.</summary>
public enum EndpointDetectionSource
{
    Unknown = 0,
    Etw = 1,
    Tcp = 2,
    Manual = 3,
}
