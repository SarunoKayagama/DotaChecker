using DotaChecker.Core.Models;
using DotaChecker.Core.Statistics;

namespace DotaChecker.Core.Diagnostics;

/// <summary>
/// Чистый статистический анализ наблюдений: по данным проб формирует измеренные значения,
/// неопределённость и диагностическую гипотезу. Не знает о UI и сетевом API.
/// </summary>
public sealed class RouteAnalyzer
{
    private const double Z95 = 1.96;

    private readonly AnalyzerOptions _options;

    public RouteAnalyzer(AnalyzerOptions? options = null) => _options = options ?? new AnalyzerOptions();

    public DiagnosisResult Analyze(AnalysisInput input)
    {
        var now = DateTimeOffset.UtcNow;
        var windows = input.Windows.Where(window => window.Hops.Count > 0).ToArray();
        var limitations = BuildLimitations();

        if (windows.Length < _options.RequiredStableWindows)
        {
            return BuildInsufficient(now, Array.Empty<HopMeasurement>(), EmptyE2e(),
                EmptyUncertainty(), limitations, "Недостаточно окон наблюдений.");
        }

        var hopIndices = windows
            .SelectMany(window => window.Hops.Keys)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

        var measurements = new List<HopMeasurement>();
        var series = new Dictionary<int, bool[]>();
        var minSamples = long.MaxValue;

        foreach (var index in hopIndices)
        {
            var successes = windows
                .SelectMany(window => window.Hops.TryGetValue(index, out var stats)
                    ? stats.Successes
                    : Array.Empty<bool>())
                .ToArray();
            var sent = successes.Length;
            var received = successes.Count(value => value);
            minSamples = Math.Min(minSamples, sent);
            series[index] = successes;
            measurements.Add(ToMeasurement(index, index.ToString(), sent, received, successes));
        }

        var e2eSuccesses = windows.SelectMany(window => window.E2e.Successes).ToArray();
        var e2e = ToMeasurement(0, "e2e", e2eSuccesses.Length, e2eSuccesses.Count(value => value), e2eSuccesses);

        if (minSamples < _options.MinSamples || measurements.Count == 0)
        {
            return BuildInsufficient(now, measurements, e2e, EmptyUncertainty(), limitations,
                $"Минимум проб на узел: {minSamples} (порог {_options.MinSamples}).");
        }

        var threshold = _options.LossThresholdPct / 100.0;
        var lossy = measurements
            .Where(item => item.HopIndex > 0 && item.CiLow > threshold)
            .Select(item => item.HopIndex)
            .ToArray();
        var ok = measurements
            .Where(item => item.HopIndex > 0 && item.CiHigh < threshold)
            .Select(item => item.HopIndex)
            .ToHashSet();

        if (lossy.Length == 0)
        {
            return BuildInsufficient(now, measurements, e2e, EmptyUncertainty(), limitations,
                "Нет узлов со статистически значимым отсутствием ответов.");
        }

        var firstLossyPerWindow = DetectFirstLossyPerWindow(windows, threshold);
        var k = lossy.Min();
        var stableWindows = firstLossyPerWindow.Count(value => value == k);
        if (stableWindows < _options.RequiredStableWindows)
        {
            return BuildInsufficient(now, measurements, e2e, EmptyUncertainty(), limitations,
                "Первый узел с отсутствием ответов нестабилен между окнами.");
        }

        var nextHop = measurements
            .Where(item => item.HopIndex > k)
            .OrderBy(item => item.HopIndex)
            .FirstOrDefault();
        var compareSeries = nextHop is not null ? series[nextHop.HopIndex] : e2eSuccesses;
        var comparison = CompareLoss(series[k], compareSeries);

        var notes = new List<string>(comparison.Notes);
        var uncertainty = new StatisticalUncertainty(
            comparison.Z,
            comparison.BootstrapLow,
            comparison.BootstrapHigh,
            comparison.Difference,
            measurements.First(item => item.HopIndex == k).Lag1Autocorrelation,
            comparison.BlockLength,
            comparison.Iterations,
            notes);

        // 1. Подавление ICMP-ответов: потеря значимо уменьшается ниже по маршруту.
        if (nextHop is not null && comparison.BootstrapLow > _options.ComparisonMargin
            && ok.Contains(nextHop.HopIndex) && e2e.CiHigh < threshold)
        {
            var verdict = new DiagnosticHypothesis(
                DiagnosisVerdict.IcmpResponseSuppression,
                LocalizationKind.Node,
                k,
                k,
                Score(measurements, k, stableWindows, windows.Length, true, minSamples),
                $"Узел {k} не отвечает на {measurements.First(item => item.HopIndex == k).LossPct:F1}% проб, " +
                $"но узел {nextHop.HopIndex} и e2e отвечают без значимых потерь. " +
                "Транзитные пакеты через узел проходят; вероятно подавление генерации ICMP Time Exceeded.",
                "Не считать этот узел источником транзитных потерь. Проверить rate-limit на маршрутизаторе.");
            return new DiagnosisResult(now, measurements, e2e, uncertainty, verdict, limitations);
        }

        // 2. Проблема только на стороне цели: ICMP не различает фильтрацию, обратный путь и последний сегмент.
        var isDestination = k == hopIndices[^1];
        if (isDestination && hopIndices.Length > 1 && hopIndices.Take(..^1).All(index => ok.Contains(index)))
        {
            var verdict = new DiagnosticHypothesis(
                DiagnosisVerdict.Probable,
                LocalizationKind.DestinationSide,
                k,
                k,
                Score(measurements, k, stableWindows, windows.Length, false, minSamples),
                "Отсутствие ответов наблюдается только у конечного узла и в e2e. " +
                "Эти измерения не позволяют отличить фильтрацию ответов цели, проблему обратного маршрута " +
                "и потери на последнем участке.",
                "Проверить доступность сервиса другим протоколом после завершения проверки UDP/TCP; " +
                "не делать вывода о транзитной потере по ICMP.");
            return new DiagnosisResult(now, measurements, e2e, uncertainty, verdict, limitations);
        }

        // 3. Потери переносятся ниже по маршруту — кандидат на участок (k−1 → k).
        // «Переносится» = не удалось доказать значимое уменьшение долей.
        // Требовать доказательства равенства было бы слишком строго для шумных данных.
        var carried = comparison.BootstrapLow <= _options.ComparisonMargin;
        if (!carried)
        {
            return BuildInsufficient(now, measurements, e2e, uncertainty, limitations,
                "Разница долей между узлами статистически неоднозначна.");
        }

        var score = Score(measurements, k, stableWindows, windows.Length,
            e2e.CiLow > threshold, minSamples);
        var lastLossy = lossy.Max();
        var firstValues = firstLossyPerWindow.Where(value => value > 0).Distinct().OrderBy(value => value).ToArray();
        var uniqueFirst = firstValues.Length == 1;
        var allStable = stableWindows == windows.Length;

        DiagnosisVerdict carriedVerdict;
        LocalizationKind localization;
        int regionFrom;
        int regionTo;

        if (input.ControlledExperiment && allStable && uniqueFirst
            && score >= _options.ControlledScoreThreshold)
        {
            carriedVerdict = DiagnosisVerdict.Confirmed;
            localization = LocalizationKind.ExactSegment;
            regionFrom = k;
            regionTo = k;
        }
        else if (uniqueFirst && score >= _options.ProbableScoreThreshold)
        {
            carriedVerdict = DiagnosisVerdict.SuspectedSegment;
            localization = LocalizationKind.ExactSegment;
            regionFrom = k;
            regionTo = k;
        }
        else
        {
            carriedVerdict = DiagnosisVerdict.ProbableRegion;
            localization = LocalizationKind.Region;
            regionFrom = firstValues.FirstOrDefault(k);
            regionTo = lastLossy;
        }

        var explanation = localization == LocalizationKind.ExactSegment
            ? $"Отсутствие ответов впервые появляется на узле {k} и сохраняется на всех последующих узлах и e2e. " +
              $"Наиболее вероятный участок — между узлами {Math.Max(1, k - 1)} и {k}."
            : $"Отсутствие ответов наблюдается в области узлов {regionFrom}…{regionTo}, " +
              "но данные не позволяют указать один сегмент.";

        var hypothesis = new DiagnosticHypothesis(
            carriedVerdict,
            localization,
            regionFrom,
            regionTo,
            score,
            explanation,
            "Сравнить с результатами других протоколов; при малом объёме выборки продолжить наблюдение.");

        return new DiagnosisResult(now, measurements, e2e, uncertainty, hypothesis, limitations);
    }

    private int[] DetectFirstLossyPerWindow(WindowSnapshot[] windows, double threshold)
    {
        var result = new int[windows.Length];
        for (var i = 0; i < windows.Length; i++)
        {
            result[i] = 0;
            foreach (var pair in windows[i].Hops.OrderBy(pair => pair.Key))
            {
                if (pair.Value.Sent == 0)
                {
                    continue;
                }

                var received = pair.Value.Received;
                var interval = Wilson(pair.Value.Sent - received, pair.Value.Sent);
                if (interval.Low > threshold)
                {
                    result[i] = pair.Key;
                    break;
                }
            }
        }

        return result;
    }

    private double Score(
        IReadOnlyList<HopMeasurement> measurements,
        int k,
        int stableWindows,
        int windowCount,
        bool e2eAgreement,
        long minSamples)
    {
        var sSample = Math.Clamp(minSamples / 50.0, 0, 1);
        var sStable = windowCount == 0 ? 0 : (double)stableWindows / windowCount;
        var measurement = measurements.First(item => item.HopIndex == k);
        var half = (measurement.CiHigh - measurement.CiLow) / 2.0;
        var p = 1.0 - (double)measurement.Received / Math.Max(1, measurement.Sent);
        // T = _options.LossThresholdPct / 100 — порог значимой probe response loss в долях единицы.
        var t = _options.LossThresholdPct / 100.0;
        var sSep = Math.Clamp((p - t) / (half + 0.05), 0, 1);
        var sE2e = e2eAgreement ? 1.0 : 0.3;
        return 0.25 * sSample + 0.25 * sStable + 0.25 * sSep + 0.25 * sE2e;
    }

    private static HopMeasurement ToMeasurement(int index, string address, long sent, long received, bool[] successes)
    {
        // Wilson строится по числу отсутствующих ответов (loss), а не по числу успехов.
        var interval = Wilson(sent - received, sent);
        return new HopMeasurement(
            index,
            address,
            sent,
            received,
            sent == 0 ? 0 : 100.0 * (sent - received) / sent,
            interval.Low,
            interval.High,
            Lag1Autocorrelation(successes),
            LongestLossRun(successes));
    }

    private static (double Low, double High) Wilson(long received, long sent)
    {
        if (sent == 0)
        {
            return (0, 1);
        }

        var p = (double)received / sent;
        var z2 = Z95 * Z95;
        var denom = 1 + z2 / sent;
        var center = (p + z2 / (2 * sent)) / denom;
        var half = Z95 / denom * Math.Sqrt(p * (1 - p) / sent + z2 / (4.0 * sent * sent));
        return (Math.Max(0, center - half), Math.Min(1, center + half));
    }

    /// <summary>Автокорреляция 1-го порядка ряда потерь (1 = нет ответа, 0 = ответ).</summary>
    private static double Lag1Autocorrelation(bool[] successes)
    {
        if (successes.Length < 3)
        {
            return 0;
        }

        var losses = successes.Select(value => value ? 0.0 : 1.0).ToArray();
        var mean = losses.Average();
        var denominator = losses.Sum(value => (value - mean) * (value - mean));
        if (denominator <= 1e-12)
        {
            return 0;
        }

        var numerator = 0.0;
        for (var i = 1; i < losses.Length; i++)
        {
            numerator += (losses[i - 1] - mean) * (losses[i] - mean);
        }

        return numerator / denominator;
    }

    private static int LongestLossRun(bool[] successes)
    {
        var best = 0;
        var current = 0;
        foreach (var success in successes)
        {
            current = success ? 0 : current + 1;
            best = Math.Max(best, current);
        }

        return best;
    }

    private ProportionComparison CompareLoss(bool[] first, bool[] second)
    {
        var a = first.Select(value => value ? 0.0 : 1.0).ToArray();
        var b = second.Select(value => value ? 0.0 : 1.0).ToArray();
        var pA = a.Length == 0 ? 0 : a.Average();
        var pB = b.Length == 0 ? 0 : b.Average();
        var difference = pA - pB;

        // z-тест — только предварительный инструмент, предполагает независимость проб.
        var se = Math.Sqrt(pA * (1 - pA) / Math.Max(1, a.Length) + pB * (1 - pB) / Math.Max(1, b.Length));
        var z = se <= 1e-12 ? 0 : (difference - _options.ComparisonMargin) / se;

        var phi = Math.Max(Lag1Autocorrelation(first), Lag1Autocorrelation(second));
        var correlated = Math.Abs(phi) > _options.AutocorrelationThreshold;
        var blockLength = correlated
            ? Math.Clamp((int)Math.Ceiling(1.0 / Math.Max(0.05, 1.0 - phi)), 2, _options.MaxBlockLength)
            : 1;

        var rng = new Random(12345);
        var bootstrap = new double[_options.BootstrapIterations];
        for (var i = 0; i < bootstrap.Length; i++)
        {
            var sampleA = ResampleBlocks(a, blockLength, rng);
            var sampleB = ResampleBlocks(b, blockLength, rng);
            bootstrap[i] = sampleA.Average() - sampleB.Average();
        }

        Array.Sort(bootstrap);
        var lowIndex = (int)(0.025 * bootstrap.Length);
        var highIndex = Math.Min(bootstrap.Length - 1, (int)(0.975 * bootstrap.Length));
        var notes = new List<string>
        {
            correlated
                ? $"Обнаружена временная корреляция (φ₁={phi:F2}); использован block bootstrap, длина блока {blockLength}."
                : $"Корреляция слабая (φ₁={phi:F2}); block bootstrap всё равно применён, z-тест — предварительный."
        };

        return new ProportionComparison(z, bootstrap[lowIndex], bootstrap[highIndex], difference,
            blockLength, bootstrap.Length, notes);
    }

    private static double[] ResampleBlocks(double[] source, int blockLength, Random random)
    {
        if (source.Length == 0)
        {
            return Array.Empty<double>();
        }

        var result = new double[source.Length];
        var written = 0;
        while (written < source.Length)
        {
            var start = random.Next(source.Length);
            var length = Math.Min(blockLength, source.Length - written);
            for (var i = 0; i < length; i++)
            {
                result[written + i] = source[(start + i) % source.Length];
            }

            written += length;
        }

        return result;
    }

    private List<string> BuildLimitations()
    {
        return new List<string>
        {
            "Отсутствие ICMP-ответа (probe response loss) не доказывает потерю транзитных пакетов.",
            "Накопленная транзитная потеря и вероятность генерации ICMP-ответа не разделяются по ICMP-наблюдениям.",
            "Обратный путь ICMP-ошибок может отличаться от прямого.",
            "e2e reference — дополнительный сигнал, а не абсолютный эталон.",
            $"Пороги T={_options.LossThresholdPct}%, δ={_options.ComparisonMargin:P0}, K={_options.RequiredStableWindows} — эвристики.",
            "EvidenceScore — мера согласованности данных, а не вероятность правильности диагноза.",
        };
    }

    private DiagnosisResult BuildInsufficient(
        DateTimeOffset now,
        IReadOnlyList<HopMeasurement> measurements,
        HopMeasurement e2e,
        StatisticalUncertainty uncertainty,
        IReadOnlyList<string> limitations,
        string reason)
    {
        return new DiagnosisResult(
            now,
            measurements,
            e2e,
            uncertainty,
            new DiagnosticHypothesis(
                DiagnosisVerdict.InsufficientData,
                LocalizationKind.None,
                0,
                0,
                0,
                reason + " Участок не определяется.",
                "Продолжить наблюдение или увеличить число проб."),
            limitations);
    }

    private static HopMeasurement EmptyE2e() => new(0, "e2e", 0, 0, 0, 0, 1, 0, 0);

    private static StatisticalUncertainty EmptyUncertainty() =>
        new(0, 0, 0, 0, 0, 1, 0, new[] { "Недостаточно данных для статистического сравнения." });

    private sealed record ProportionComparison(
        double Z,
        double BootstrapLow,
        double BootstrapHigh,
        double Difference,
        int BlockLength,
        int Iterations,
        IReadOnlyList<string> Notes);
}
