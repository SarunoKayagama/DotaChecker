namespace DotaChecker.Core.Optimization;

/// <summary>
/// Чистое сравнение Direct и WARP по одним и тем же endpoint/протоколу/условиям.
/// Не запускает процессы, не меняет маршруты и не делает вывод при неподтверждённом пути.
/// </summary>
public sealed class RouteOptimizer
{
    private const int MinimumSamples = 20;
    private const double LossSignificance = 0.03;
    private const double JitterRelativeThreshold = 0.20;
    private const double RttRelativeThreshold = 0.15;

    public RouteValidationResult Validate(RouteMeasurement? direct, RouteMeasurement? warp)
    {
        if (direct is null || warp is null)
        {
            return Invalid("Нет измерений Direct или WARP.");
        }

        var warnings = new List<string>();
        if (!string.Equals(direct.Candidate.EndpointKey, warp.Candidate.EndpointKey, StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("Измерения относятся к разным endpoint.");
        }

        if (!string.Equals(direct.Protocol, warp.Protocol, StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("Протоколы измерений различаются.");
        }

        if (!direct.Candidate.IsInterfaceVerified || !warp.Candidate.IsInterfaceVerified)
        {
            return Invalid("Фактический интерфейс хотя бы одного измерения не подтверждён.");
        }

        if (direct.Candidate.InterfaceIndex == warp.Candidate.InterfaceIndex)
        {
            return Invalid("Оба измерения прошли через один интерфейс: WARP-путь не подтверждён.");
        }

        if (!warp.Candidate.UsesSameEndpoint)
        {
            return Invalid("WARP-измерение выполнено до другого назначения.");
        }

        if (direct.Sent < MinimumSamples || warp.Sent < MinimumSamples)
        {
            return Invalid($"Недостаточно проб: минимум {MinimumSamples} на вариант.");
        }

        if (Math.Abs(direct.Sent - warp.Sent) > Math.Max(5, direct.Sent * 0.1))
        {
            warnings.Add("Число проб отличается более чем на 10%.");
        }

        if (direct.Duration <= TimeSpan.Zero || warp.Duration <= TimeSpan.Zero ||
            direct.Duration.TotalSeconds / warp.Duration.TotalSeconds is < 0.5 or > 2.0)
        {
            warnings.Add("Окна измерений существенно различаются.");
        }

        if (Math.Abs((direct.StartedAt - warp.StartedAt).TotalMinutes) > 10)
        {
            warnings.Add("Измерения разделены более чем на 10 минут.");
        }

        return new RouteValidationResult(true, "Условия сопоставимы.", warnings.ToArray());
    }

    public RouteComparison Compare(RouteMeasurement? direct, RouteMeasurement? warp)
    {
        var validation = Validate(direct, warp);
        var now = DateTimeOffset.UtcNow;
        if (!validation.IsValid || direct is null || warp is null)
        {
            return new RouteComparison(
                RouteComparisonStatus.RouteComparisonInvalid,
                direct?.Candidate.EndpointKey ?? warp?.Candidate.EndpointKey ?? string.Empty,
                null,
                null,
                null,
                validation,
                new[] { validation.Reason },
                now,
                "Сравнение недействительно: " + validation.Reason);
        }

        var directScore = Score(direct);
        var warpScore = Score(warp);
        var reasons = new List<string>(validation.Warnings);

        var lossA = LossProportion(direct);
        var lossB = LossProportion(warp);
        var ciA = Wilson(lossA, direct.Sent);
        var ciB = Wilson(lossB, warp.Sent);
        var lossDifference = lossA - lossB;
        var lossSeparated = ciA.Low > ciB.High + LossSignificance || ciB.Low > ciA.High + LossSignificance;

        if (lossSeparated)
        {
            var winner = lossA < lossB ? direct.Candidate : warp.Candidate;
            var status = lossA < lossB ? RouteComparisonStatus.Better : RouteComparisonStatus.Worse;
            reasons.Add($"Отсутствие ICMP-ответов различается на {Math.Abs(lossDifference):P1} (это не доказывает потерю игрового UDP).");
            return Result(status, direct, warp, directScore, warpScore, winner, validation, reasons, now,
                "Решение по доставке ответов; приоритет метрики — стабильность доставки.");
        }

        var jitterDifference = direct.JitterMs - warp.JitterMs;
        if (Math.Abs(jitterDifference) / Math.Max(1.0, Math.Max(direct.JitterMs, warp.JitterMs)) > JitterRelativeThreshold)
        {
            var winner = jitterDifference > 0 ? warp.Candidate : direct.Candidate;
            var status = jitterDifference > 0 ? RouteComparisonStatus.Worse : RouteComparisonStatus.Better;
            reasons.Add($"Jitter различается на {Math.Abs(jitterDifference):F1} мс.");
            return Result(status, direct, warp, directScore, warpScore, winner, validation, reasons, now,
                "Решение по jitter при неразличимом отсутствии ответов.");
        }

        var medianA = Median(direct.RttMs);
        var medianB = Median(warp.RttMs);
        if (Math.Abs(medianA - medianB) / Math.Max(1.0, Math.Max(medianA, medianB)) > RttRelativeThreshold)
        {
            var winner = medianA < medianB ? direct.Candidate : warp.Candidate;
            var status = medianA < medianB ? RouteComparisonStatus.Better : RouteComparisonStatus.Worse;
            reasons.Add($"Median RTT различается на {Math.Abs(medianA - medianB):F1} мс.");
            return Result(status, direct, warp, directScore, warpScore, winner, validation, reasons, now,
                "Решение по median RTT — только после доставки и jitter.");
        }

        reasons.Add("Статистически значимых различий по доставке, jitter и RTT нет.");
        return new RouteComparison(
            RouteComparisonStatus.NoSignificantDifference,
            direct.Candidate.EndpointKey,
            null,
            directScore,
            warpScore,
            validation,
            reasons.ToArray(),
            now,
            "Разница не доказана; выбор по минимальному ping не производится.");
    }

    private static RouteComparison Result(
        RouteComparisonStatus status,
        RouteMeasurement direct,
        RouteMeasurement warp,
        RouteQualityScore directScore,
        RouteQualityScore warpScore,
        RouteCandidate winner,
        RouteValidationResult validation,
        IReadOnlyList<string> reasons,
        DateTimeOffset now,
        string explanation) =>
        new(status, direct.Candidate.EndpointKey, winner, directScore, warpScore, validation,
            reasons.ToArray(), now, explanation);

    private static RouteQualityScore Score(RouteMeasurement measurement)
    {
        var loss = Math.Clamp(measurement.ResponseLossPct / 100.0, 0, 1);
        var delivery = 1 - loss;
        var jitter = 1 / (1 + Math.Max(0, measurement.JitterMs) / 20.0);
        var rtt = 1 / (1 + Math.Max(0, Median(measurement.RttMs)) / 100.0);
        var stability = 1 - (double)measurement.LongestLossRun / Math.Max(1, measurement.Sent);
        var overall = 0.50 * delivery + 0.25 * jitter + 0.15 * rtt + 0.10 * Math.Clamp(stability, 0, 1);
        return new RouteQualityScore(delivery, jitter, rtt, stability, overall);
    }

    private static double LossProportion(RouteMeasurement measurement) =>
        measurement.Sent == 0 ? 0 : (double)(measurement.Sent - measurement.Received) / measurement.Sent;

    private static double Median(double[] values)
    {
        if (values.Length == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(value => value).ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2.0 : sorted[middle];
    }

    private static (double Low, double High) Wilson(double proportion, int samples)
    {
        if (samples <= 0)
        {
            return (0, 1);
        }

        const double z = 1.96;
        var z2 = z * z;
        var denom = 1 + z2 / samples;
        var center = (proportion + z2 / (2 * samples)) / denom;
        var half = z / denom * Math.Sqrt(proportion * (1 - proportion) / samples + z2 / (4.0 * samples * samples));
        return (Math.Max(0, center - half), Math.Min(1, center + half));
    }

    private static RouteValidationResult Invalid(string reason) =>
        new(false, reason, Array.Empty<string>());
}
