namespace DotaChecker.Core.Game;

/// <summary>
/// Категория наблюдаемого endpoint. Намеренно нет ConfirmedGameServer:
/// реальный игровой сервер может быть скрыт за Steam Datagram Relay.
/// </summary>
public enum GameEndpointCategory
{
    Unknown = 0,
    LikelyGameRelay = 1,
    SteamCoordinator = 2,
    Voice = 3,
    Other = 4,
}
