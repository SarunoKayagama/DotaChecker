namespace DotaChecker.Core.Optimization;

public enum RouteCandidateKind
{
    Direct,
    Warp,
    Alternative,
}

public enum RouteComparisonStatus
{
    NotTested,
    Testing,
    Better,
    Worse,
    NoSignificantDifference,
    InsufficientData,
    RouteComparisonInvalid,
    Error,
}

public sealed record RouteCandidate(
    RouteCandidateKind Kind,
    string Name,
    string EndpointKey,
    string DestinationAddress,
    int InterfaceIndex,
    string InterfaceName,
    string SourceAddress,
    string TunnelState,
    bool IsInterfaceVerified,
    bool UsesSameEndpoint);

public sealed record RouteMeasurement(
    RouteCandidate Candidate,
    string Protocol,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    int Sent,
    int Received,
    double[] RttMs,
    double ResponseLossPct,
    double JitterMs,
    int LongestLossRun);

public sealed record RouteValidationResult(
    bool IsValid,
    string Reason,
    IReadOnlyList<string> Warnings);

public sealed record RouteQualityScore(
    double DeliveryScore,
    double JitterScore,
    double RttScore,
    double StabilityScore,
    double Overall)
{
    public string Explanation => "Overall — ранжирующая мера, а не вероятность правильности выбора.";
}

public sealed record RouteComparison(
    RouteComparisonStatus Status,
    string EndpointKey,
    RouteCandidate? Winner,
    RouteQualityScore? DirectScore,
    RouteQualityScore? WarpScore,
    RouteValidationResult Validation,
    IReadOnlyList<string> Reasons,
    DateTimeOffset ComparedAt,
    string Explanation);
