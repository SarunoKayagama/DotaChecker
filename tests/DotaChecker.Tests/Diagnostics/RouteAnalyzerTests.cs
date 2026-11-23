using System.Net;
using DotaChecker.Core.Diagnostics;
using DotaChecker.Core.Models;
using DotaChecker.Core.Statistics;

namespace DotaChecker.Tests.Diagnostics;

public sealed class RouteAnalyzerTests
{
    private static readonly IPAddress Target = IPAddress.Parse("198.51.100.10");
    private const int ProbesPerWindow = 60;

    private readonly RouteAnalyzer _analyzer = new(new AnalyzerOptions
    {
        MinSamples = 20,
        LossThresholdPct = 5,
        RequiredStableWindows = 2,
        BootstrapIterations = 500,
    });

    [Fact]
    public void ScenarioA_TransitLoss_ProducesSuspectedSegment()
    {
        // Потеря 20% на сегменте 2→3: hop3, hop4, hop5 и e2e теряют ответы.
        var input = Build(new[] { 0.0, 0.0, 0.20, 0.20, 0.20 }, 0.20);

        var result = _analyzer.Analyze(input);

        result.Hypothesis.Verdict.Should().Be(DiagnosisVerdict.SuspectedSegment);
        result.Hypothesis.Localization.Should().Be(LocalizationKind.ExactSegment);
        result.SegmentLabel.Should().Contain("2 → 3");
        result.Hypothesis.Verdict.Should().NotBe(DiagnosisVerdict.Confirmed);
        result.Measurements.Should().HaveCount(5);
        result.Limitations.Should().NotBeEmpty();
    }

    [Fact]
    public void ScenarioA_ControlledExperiment_AllowsConfirmed()
    {
        var input = Build(new[] { 0.0, 0.0, 0.20, 0.20, 0.20 }, 0.20, controlled: true);

        var result = _analyzer.Analyze(input);

        result.Hypothesis.Verdict.Should().Be(DiagnosisVerdict.Confirmed);
        result.Hypothesis.Localization.Should().Be(LocalizationKind.ExactSegment);
    }

    [Fact]
    public void ScenarioB_IcmpSuppression_IsNotTransitLoss()
    {
        // Только hop2 подавляет ICMP Time Exceeded; транзит и остальные узлы работают.
        var input = Build(new[] { 0.0, 0.30, 0.0, 0.0, 0.0 }, 0.0);

        var result = _analyzer.Analyze(input);

        result.Hypothesis.Verdict.Should().Be(DiagnosisVerdict.IcmpResponseSuppression);
        result.SegmentLabel.Should().Contain("2");
    }

    [Fact]
    public void ScenarioC_DestinationOnly_ReportsDestinationSideProbable()
    {
        // hop5 и e2e теряют ответы; верхние узлы чисты. ICMP не различает причину.
        var input = Build(new[] { 0.0, 0.0, 0.0, 0.0, 0.15 }, 0.15);

        var result = _analyzer.Analyze(input);

        result.Hypothesis.Verdict.Should().Be(DiagnosisVerdict.Probable);
        result.Hypothesis.Localization.Should().Be(LocalizationKind.DestinationSide);
        result.SegmentLabel.Should().NotContain("4 → 5");
        result.Hypothesis.Explanation.Should().Contain("не позволяют отличить");
    }

    [Fact]
    public void ScenarioF_TooFewSamples_ReturnsInsufficientData()
    {
        var input = Build(new[] { 0.0, 0.0, 0.20, 0.20, 0.20 }, 0.20, probesPerWindow: 2, windows: 4);

        var result = _analyzer.Analyze(input);

        result.Hypothesis.Verdict.Should().Be(DiagnosisVerdict.InsufficientData);
        result.Hypothesis.Localization.Should().Be(LocalizationKind.None);
        result.SegmentLabel.Should().NotContain("→");
    }

    [Fact]
    public void ScenarioI_BurstyLosses_AreNotTreatedAsUniform()
    {
        // Серии последовательных пропусков, а не равномерные потери.
        var input = Build(new[] { 0.0, 0.0, 0.25, 0.25, 0.25 }, 0.25, bursty: true);

        var result = _analyzer.Analyze(input);

        result.Hypothesis.Verdict.Should().NotBe(DiagnosisVerdict.Confirmed);
        result.Uncertainty.Notes.Should().Contain(note => note.Contains("корреляц"));
        result.Uncertainty.BlockLength.Should().BeGreaterThan(1);
        var hop3 = result.Measurements.Single(item => item.HopIndex == 3);
        hop3.Lag1Autocorrelation.Should().BeGreaterThan(0.2);
        hop3.LongestLossRun.Should().BeGreaterThanOrEqualTo(10);
        result.Limitations.Should().Contain(item => item.Contains("транзитных пакетов"));
    }

    [Fact]
    public void Result_SeparatesMeasurementsUncertaintyAndHypothesis()
    {
        var result = _analyzer.Analyze(Build(new[] { 0.0, 0.0, 0.20, 0.20, 0.20 }, 0.20));

        result.Measurements.Should().OnlyContain(item => item.CiLow <= item.CiHigh);
        result.Uncertainty.BootstrapIterations.Should().Be(500);
        result.Uncertainty.BootstrapDifferenceLow.Should().BeLessThan(result.Uncertainty.BootstrapDifferenceHigh);
        result.Hypothesis.EvidenceScore.Should().BeInRange(0, 1);
        result.ScoreText.Should().Contain("не вероятность");
    }

    private static AnalysisInput Build(
        double[] lossProbabilities,
        double e2eLoss,
        bool bursty = false,
        int windows = 4,
        int probesPerWindow = ProbesPerWindow,
        bool controlled = false)
    {
        var hopCount = lossProbabilities.Length;
        var hops = Enumerable.Range(1, hopCount)
            .Select(index => new RouteHop(
                index,
                new[] { IPAddress.Parse($"10.0.{index}.1") },
                index == hopCount))
            .ToArray();
        var route = new RouteSnapshot(Target, hops, DateTimeOffset.UtcNow);

        var snapshots = new List<WindowSnapshot>();
        var start = DateTimeOffset.UtcNow.AddSeconds(-windows * 10);

        for (var window = 0; window < windows; window++)
        {
            var perHop = new Dictionary<int, HopWindowStats>();
            for (var index = 1; index <= hopCount; index++)
            {
                var outcomes = Generate(probesPerWindow, lossProbabilities[index - 1], bursty,
                    1000 + index * 37 + window);
                perHop[index] = new HopWindowStats(
                    index, outcomes.Length, outcomes.Count(value => value), outcomes);
            }

            var e2eOutcomes = Generate(probesPerWindow, e2eLoss, bursty, 7000 + window);
            snapshots.Add(new WindowSnapshot(
                start,
                start.AddSeconds(10),
                false,
                perHop,
                new HopWindowStats(0, e2eOutcomes.Length, e2eOutcomes.Count(value => value), e2eOutcomes)));
            start = start.AddSeconds(10);
        }

        return new AnalysisInput(snapshots, route, ProbeProtocol.Icmp, controlled);
    }

    private static bool[] Generate(int count, double lossProbability, bool bursty, int seed)
    {
        var outcomes = Enumerable.Repeat(true, count).ToArray();
        if (bursty)
        {
            var lossCount = (int)Math.Round(count * lossProbability);
            var start = count / 4;
            for (var i = start; i < Math.Min(count, start + lossCount); i++)
            {
                outcomes[i] = false;
            }

            return outcomes;
        }

        var random = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            outcomes[i] = random.NextDouble() >= lossProbability;
        }

        return outcomes;
    }
}
