namespace DotaChecker.Core.Warp;

/// <summary>Публичная информация о .conf профиле. Секреты не хранятся.</summary>
public sealed record AmneziaWgProfileInfo(
    string Name,
    string FilePath,
    bool FileExists,
    bool IsStructurallyValid,
    string? PublicEndpoint,
    int AddressCount,
    int AllowedIpCount,
    int Mtu,
    bool HasPrivateKeyField,
    bool HasAmneziaSpecificFields,
    IReadOnlyList<string> Warnings)
{
    public string SecretSummary => HasPrivateKeyField ? "PrivateKey present (не отображается)" : "PrivateKey absent";
}

public enum AmneziaWgTunnelState
{
    Unknown,
    NotFound,
    Stopped,
    Running,
    PermissionDenied,
    Error,
}

public sealed record AmneziaWgTunnelStatus(
    string Name,
    AmneziaWgTunnelState State,
    string ServiceStatus,
    string LastHandshake,
    string Transfer,
    string Error);
