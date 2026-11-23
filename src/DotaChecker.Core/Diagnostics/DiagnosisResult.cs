namespace DotaChecker.Core.Diagnostics;

/// <summary>Как именно локализована гипотеза.</summary>
public enum LocalizationKind
{
    None = 0,
    Node = 4,
    ExactSegment = 1,
    Region = 2,
    DestinationSide = 3,
}

/// <summary>Измеренные значения без интерпретации.</summary>
/// <param name="HopIndex">Номер узла; 0 означает e2e reference.</param>
/// <param name="AddressText">Адрес или адреса ответивших узлов.</param>
/// <param name="Sent">Отправлено проб.</param>
/// <param name="Received">Получено ответов.</param>
/// <param name="LossPct">Доля отсутствующих ответов, %.</param>
/// <param name="CiLow">Нижняя граница 95% Wilson CI для доли потерь.</param>
/// <param name="CiHigh">Верхняя граница 95% Wilson CI для доли потерь.</param>
/// <param name="Lag1Autocorrelation">Автокорреляция 1-го порядка ряда «ответ/нет ответа».</param>
/// <param name="LongestLossRun">Самая длинная серия подряд отсутствующих ответов.</param>
public sealed record HopMeasurement(
    int HopIndex,
    string AddressText,
    long Sent,
    long Received,
    double LossPct,
    double CiLow,
    double CiHigh,
    double Lag1Autocorrelation,
    int LongestLossRun);

/// <summary>Статистическая неопределённость и результаты проверок.</summary>
public sealed record StatisticalUncertainty(
    double PreliminaryZ,
    double BootstrapDifferenceLow,
    double BootstrapDifferenceHigh,
    double ComparedHopLossDifference,
    double Lag1Autocorrelation,
    int BlockLength,
    int BootstrapIterations,
    IReadOnlyList<string> Notes);

/// <summary>Диагностическая гипотеза. EvidenceScore — мера согласованности данных, не вероятность.</summary>
public sealed record DiagnosticHypothesis(
    DiagnosisVerdict Verdict,
    LocalizationKind Localization,
    int FirstAffectedHop,
    int LastAffectedHop,
    double EvidenceScore,
    string Explanation,
    string Recommendation);

/// <summary>
/// Результат анализа. Измерения, неопределённость и гипотеза показаны отдельно,
/// чтобы пользователь видел разницу между наблюдаемыми числами и выводом.
/// </summary>
public sealed record DiagnosisResult(
    DateTimeOffset AnalyzedAt,
    IReadOnlyList<HopMeasurement> Measurements,
    HopMeasurement E2eMeasurement,
    StatisticalUncertainty Uncertainty,
    DiagnosticHypothesis Hypothesis,
    IReadOnlyList<string> Limitations)
{
    public string SegmentLabel => Hypothesis.Localization switch
    {
        LocalizationKind.ExactSegment =>
            $"участок {Math.Max(1, Hypothesis.FirstAffectedHop - 1)} → {Hypothesis.FirstAffectedHop}",
        LocalizationKind.Region =>
            $"область {Hypothesis.FirstAffectedHop} … {Hypothesis.LastAffectedHop}",
        LocalizationKind.DestinationSide => "сторона назначения",
        LocalizationKind.Node => $"узел {Hypothesis.FirstAffectedHop}",
        _ => "не определена",
    };

    public string ScoreText => $"степень согласованности данных: {Hypothesis.EvidenceScore:F2} (не вероятность)";
}
