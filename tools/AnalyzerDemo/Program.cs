using System.Net;
using DotaChecker.Core.Diagnostics;
using DotaChecker.Core.Models;
using DotaChecker.Core.Statistics;

var analyzer = new RouteAnalyzer(new AnalyzerOptions());
var target = IPAddress.Parse("198.51.100.10");

var scenarios = new (string Name, double[] Loss, double E2e, bool Bursty, int Probes, bool Controlled)[]
{
    ("A transit loss 20% (2->3)", new[] { 0.0, 0.0, 0.20, 0.20, 0.20 }, 0.20, false, 60, false),
    ("A controlled experiment", new[] { 0.0, 0.0, 0.20, 0.20, 0.20 }, 0.20, false, 60, true),
    ("B ICMP suppression 30% at hop2", new[] { 0.0, 0.30, 0.0, 0.0, 0.0 }, 0.0, false, 60, false),
    ("C destination reply loss 15%", new[] { 0.0, 0.0, 0.0, 0.0, 0.15 }, 0.15, false, 60, false),
    ("F insufficient data (2 probes)", new[] { 0.0, 0.0, 0.20, 0.20, 0.20 }, 0.20, false, 2, false),
    ("I bursty correlated loss 25%", new[] { 0.0, 0.0, 0.25, 0.25, 0.25 }, 0.25, true, 60, false),
};

foreach (var scenario in scenarios)
{
    var input = Build(target, scenario.Loss, scenario.E2e, scenario.Bursty, scenario.Probes, scenario.Controlled);
    var result = analyzer.Analyze(input);
    var h = result.Hypothesis;
    Console.WriteLine($"{scenario.Name,-34} verdict={h.Verdict,-24} loc={result.SegmentLabel,-22} score={h.EvidenceScore:F2}");
    Console.WriteLine($"{"",-34} bootstrap Δ=[{result.Uncertainty.BootstrapDifferenceLow:P1}…{result.Uncertainty.BootstrapDifferenceHigh:P1}] block={result.Uncertainty.BlockLength} φ₁={result.Uncertainty.Lag1Autocorrelation:F2}");
}

static AnalysisInput Build(IPAddress target, double[] loss, double e2eLoss, bool bursty, int probes, bool controlled)
{
    var hops = Enumerable.Range(1, loss.Length)
        .Select(i => new RouteHop(i, new[] { IPAddress.Parse($"10.0.{i}.1") }, i == loss.Length))
        .ToArray();
    var route = new RouteSnapshot(target, hops, DateTimeOffset.UtcNow);
    var windows = new List<WindowSnapshot>();
    var start = DateTimeOffset.UtcNow.AddSeconds(-40);

    for (var window = 0; window < 4; window++)
    {
        var perHop = new Dictionary<int, HopWindowStats>();
        for (var i = 1; i <= loss.Length; i++)
        {
            var outcomes = Generate(probes, loss[i - 1], bursty, 1000 + i * 37 + window);
            perHop[i] = new HopWindowStats(i, outcomes.Length, outcomes.Count(x => x), outcomes);
        }

        var e2e = Generate(probes, e2eLoss, bursty, 7000 + window);
        windows.Add(new WindowSnapshot(start, start.AddSeconds(10), false, perHop,
            new HopWindowStats(0, e2e.Length, e2e.Count(x => x), e2e)));
        start = start.AddSeconds(10);
    }

    return new AnalysisInput(windows, route, ProbeProtocol.Icmp, controlled);
}

static bool[] Generate(int count, double loss, bool bursty, int seed)
{
    var outcomes = Enumerable.Repeat(true, count).ToArray();
    if (bursty)
    {
        var start = count / 4;
        for (var i = start; i < Math.Min(count, start + (int)Math.Round(count * loss)); i++)
        {
            outcomes[i] = false;
        }

        return outcomes;
    }

    var random = new Random(seed);
    for (var i = 0; i < count; i++)
    {
        outcomes[i] = random.NextDouble() >= loss;
    }

    return outcomes;
}
