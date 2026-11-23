namespace DotaChecker.Core.Diagnostics;

/// <summary>Пороги и параметры анализатора. Все значения настраиваемые и документированные.</summary>
public sealed class AnalyzerOptions
{
    /// <summary>T — порог значимой probe response loss, %.</summary>
    public double LossThresholdPct { get; set; } = 5.0;

    /// <summary>δ — защитный зазор при сравнении долей, в долях единицы.</summary>
    public double ComparisonMargin { get; set; } = 0.03;

    /// <summary>Минимальное число проб на узел для вывода.</summary>
    public int MinSamples { get; set; } = 20;

    /// <summary>Число окон, используемых для проверки устойчивости (K).</summary>
    public int RequiredStableWindows { get; set; } = 2;

    /// <summary>Порог EvidenceScore для Probable/Suspected.</summary>
    public double ProbableScoreThreshold { get; set; } = 0.50;

    /// <summary>Порог EvidenceScore для контролируемого эксперимента.</summary>
    public double ControlledScoreThreshold { get; set; } = 0.85;

    /// <summary>Число итераций block bootstrap.</summary>
    public int BootstrapIterations { get; set; } = 500;

    /// <summary>Максимальная длина блока при коррелированных пробах.</summary>
    public int MaxBlockLength { get; set; } = 10;

    /// <summary>Порог автокорреляции, выше которого независимость prob отвергается.</summary>
    public double AutocorrelationThreshold { get; set; } = 0.20;
}
