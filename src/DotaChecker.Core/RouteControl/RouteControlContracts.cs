namespace DotaChecker.Core.RouteControl;

/// <summary>
/// Контракты будущего Route Controller. Текущая реализация отсутствует намеренно:
/// автоматическое изменение системных маршрутов, DNS и туннелей не выполняется.
/// </summary>
public interface IRouteController
{
    RouteControlPrecondition CheckPreconditions();

    RouteControlPlan BuildPlan(RouteControlRequest request);

    RouteControlSnapshot ReadState();
}

public sealed record RouteControlRequest(
    string ProfileName,
    string EndpointKey,
    bool UserConfirmed);

public sealed record RouteControlPrecondition(
    bool CanProceed,
    bool RequiresElevation,
    bool RequiresUserConfirmation,
    IReadOnlyList<string> Blockers);

public sealed record RouteControlPlan(
    bool RequiresUserConfirmation,
    bool RequiresElevation,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> RollbackSteps,
    string Explanation);

public sealed record RouteControlSnapshot(
    string ActiveMode,
    string ActiveInterface,
    bool TunnelRunning,
    bool RouteVerified,
    IReadOnlyList<string> Warnings);

public sealed record RouteControlResult(
    bool Success,
    string Message,
    RouteControlSnapshot? Before,
    RouteControlSnapshot? After,
    IReadOnlyList<string> Errors);
