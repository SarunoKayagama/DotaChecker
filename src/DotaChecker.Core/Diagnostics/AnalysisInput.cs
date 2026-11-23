using DotaChecker.Core.Models;
using DotaChecker.Core.Statistics;

namespace DotaChecker.Core.Diagnostics;

/// <summary>
/// Вход анализатора. ControlledExperiment разрешает verdict Confirmed и должен
/// выставляться только для сценариев с известной ground truth.
/// </summary>
public sealed record AnalysisInput(
    IReadOnlyList<WindowSnapshot> Windows,
    RouteSnapshot Route,
    ProbeProtocol Protocol,
    bool ControlledExperiment = false);
