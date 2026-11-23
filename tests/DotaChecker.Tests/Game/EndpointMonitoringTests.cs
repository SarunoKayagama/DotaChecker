using DotaChecker.Core.Game;

namespace DotaChecker.Tests.Game;

public sealed class EndpointMonitoringTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;

    public EndpointMonitoringTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "dota-checker-monitoring", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "monitoring.json");
    }

    [Fact]
    public void SummaryRoundTrip_AndDuplicateReplacement()
    {
        var history = new EndpointMonitoringHistory(_path);
        history.Upsert(Summary("key-1", 10, 10, 20));
        history.Upsert(Summary("key-1", 20, 18, 25));

        var summaries = history.Summaries();
        summaries.Should().ContainSingle();
        summaries[0].Sent.Should().Be(20);
        history.Save();
        new EndpointMonitoringHistory(_path).Summaries().Should().ContainSingle();
    }

    [Fact]
    public void CorruptedHistory_IsBackedUp()
    {
        File.WriteAllText(_path, "{ broken");

        var history = new EndpointMonitoringHistory(_path);

        history.Summaries().Should().BeEmpty();
        Directory.GetFiles(_directory, "monitoring.json.corrupt.*").Should().ContainSingle();
        File.ReadAllText(_path).Should().Be("{ broken");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static EndpointMonitoringSummary Summary(string key, int sent, int received, double avg) =>
        new(key, "203.0.113.9", sent, received, 100.0 * (sent - received) / sent,
            avg, 3, avg, new[] { (double)avg }, DateTimeOffset.UtcNow, "active");
}
