namespace DotaChecker.Core.Optimization;

/// <summary>Хранит измерения Direct/WARP и вычисляет сравнение. Не запускает сеть.</summary>
public sealed class RouteComparisonService
{
    private readonly RouteOptimizer _optimizer = new();

    public event Action<RouteComparison>? ComparisonChanged;

    public RouteMeasurement? Direct { get; private set; }

    public RouteMeasurement? Warp { get; private set; }

    public RouteComparison? LastComparison { get; private set; }

    public void SetMeasurement(RouteCandidateKind kind, RouteMeasurement measurement)
    {
        if (kind == RouteCandidateKind.Direct)
        {
            Direct = measurement;
        }
        else if (kind == RouteCandidateKind.Warp)
        {
            Warp = measurement;
        }
        else
        {
            return;
        }

        Invalidate(kind);
    }

    public void Clear(RouteCandidateKind? kind = null)
    {
        if (kind is null || kind == RouteCandidateKind.Direct)
        {
            Direct = null;
        }

        if (kind is null || kind == RouteCandidateKind.Warp)
        {
            Warp = null;
        }

        LastComparison = new RouteComparison(
            RouteComparisonStatus.NotTested,
            string.Empty,
            null,
            null,
            null,
            new RouteValidationResult(false, "Измерения не заданы.", Array.Empty<string>()),
            Array.Empty<string>(),
            DateTimeOffset.UtcNow,
            "Ожидание измерений.");
        ComparisonChanged?.Invoke(LastComparison);
    }

    public RouteComparison Compare()
    {
        LastComparison = _optimizer.Compare(Direct, Warp);
        ComparisonChanged?.Invoke(LastComparison);
        return LastComparison;
    }

    private void Invalidate(RouteCandidateKind changed)
    {
        LastComparison = new RouteComparison(
            RouteComparisonStatus.Testing,
            changed == RouteCandidateKind.Direct
                ? Direct?.Candidate.EndpointKey ?? string.Empty
                : Warp?.Candidate.EndpointKey ?? string.Empty,
            null,
            null,
            null,
            new RouteValidationResult(false, "Измерение обновлено; требуется повторное сравнение.", Array.Empty<string>()),
            Array.Empty<string>(),
            DateTimeOffset.UtcNow,
            "Измерение обновлено.");
        ComparisonChanged?.Invoke(LastComparison);
    }
}
