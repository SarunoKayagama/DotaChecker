using System.Net;
using DotaChecker.Core.Game;
using DotaChecker.Core.Models;

namespace DotaChecker.Tests.Game;

public sealed class GameEndpointHistoryTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;

    public GameEndpointHistoryTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "dota-checker-history", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "game-endpoints.json");
    }

    [Fact]
    public void RepeatedObservation_MergesAndKeepsFirstLast()
    {
        var history = new GameEndpointHistory(_path);

        history.UpsertObservation(Observation(Start, Start.AddSeconds(10), send: 100, receive: 50));
        history.UpsertObservation(Observation(Start.AddSeconds(5), Start.AddSeconds(20), send: 150, receive: 80));

        var record = history.Records().Should().ContainSingle().Subject;
        record.FirstSeen.Should().Be(Start);
        record.LastSeen.Should().Be(Start.AddSeconds(20));
        record.SendCount.Should().Be(150);
        record.ReceiveCount.Should().Be(80);
        record.ObservationCount.Should().Be(8);
    }

    [Fact]
    public void SaveThenReload_PreservesFactsAndClassification()
    {
        var history = new GameEndpointHistory(_path);
        history.UpsertObservation(Observation(Start, Start.AddMinutes(2), send: 400, receive: 500));
        history.Reclassify(new GameEndpointClassifier());
        history.Save();

        var reloaded = new GameEndpointHistory(_path);
        var record = reloaded.Records().Should().ContainSingle().Subject;

        record.ProcessId.Should().Be(24568);
        record.Classification.Should().NotBeNull();
        record.Classification!.Category.Should().Be(GameEndpointCategory.LikelyGameRelay);
        record.Classification.ClassifierVersion.Should().Be(GameEndpointClassifier.Version);
    }

    [Fact]
    public void CorruptedJson_FallsBackToEmptyHistoryAndKeepsBackup()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_path, "{ broken json");

        var history = new GameEndpointHistory(_path);

        history.Records().Should().BeEmpty();
        Directory.GetFiles(_directory, "game-endpoints.json.corrupt.*").Should().ContainSingle();
        File.ReadAllText(_path).Should().Be("{ broken json");
    }

    [Fact]
    public void ClassificationHistory_IsPersisted()
    {
        var history = new GameEndpointHistory(_path);
        history.UpsertObservation(Observation(Start, Start.AddMinutes(2), send: 400, receive: 500));
        history.Reclassify(new GameEndpointClassifier());
        history.Reclassify(new GameEndpointClassifier());
        history.Save();

        var reloaded = new GameEndpointHistory(_path);
        reloaded.Records().Single().ClassificationHistory.Should().HaveCount(1);
        reloaded.Records().Single().ObservationTimes.Should().NotBeEmpty();
    }

    [Fact]
    public void SaveIsAtomic_WithoutTemporaryLeftovers()
    {
        var history = new GameEndpointHistory(_path);
        history.UpsertObservation(Observation(Start, Start.AddSeconds(5)));

        history.Save();

        File.Exists(_path).Should().BeTrue();
        File.Exists(_path + ".tmp").Should().BeFalse();
        File.ReadAllText(_path).Should().Contain("\"SchemaVersion\"");
    }

    [Fact]
    public void CapacityLimit_RemovesOldestEntries()
    {
        var history = new GameEndpointHistory(_path, maxEntries: 2);
        for (var i = 0; i < 5; i++)
        {
            history.UpsertObservation(Observation(
                Start.AddSeconds(i),
                Start.AddSeconds(i + 1),
                remotePort: 27000 + i));
        }

        history.RemoveStale(Start.AddMinutes(10)).Should().Be(3);
        history.Records().Should().HaveCount(2);
        history.Records().Should().NotContain(record => record.RemotePort == 27000);
    }

    [Fact]
    public void StaleEntriesAreRemoved()
    {
        var history = new GameEndpointHistory(_path, staleAfter: TimeSpan.FromMinutes(1));
        history.UpsertObservation(Observation(Start, Start.AddSeconds(10)));

        history.RemoveStale(Start.AddMinutes(5)).Should().Be(1);
        history.Records().Should().BeEmpty();
    }

    [Fact]
    public void ManualExcludeAndInclude_ArePersisted()
    {
        var history = new GameEndpointHistory(_path);
        var observation = Observation(Start, Start.AddSeconds(10));
        history.UpsertObservation(observation);

        history.Exclude(observation.Key).Should().BeTrue();
        history.Save();
        new GameEndpointHistory(_path).Records().Single().ManuallyExcluded.Should().BeTrue();

        history.Include(observation.Key).Should().BeTrue();
        history.Save();
        var included = new GameEndpointHistory(_path).Records().Single();
        included.ManuallyExcluded.Should().BeFalse();
        included.ManuallyIncluded.Should().BeTrue();
    }

    [Fact]
    public void Reclassify_RecomputesCategoriesWithoutLosingFacts()
    {
        var history = new GameEndpointHistory(_path);
        history.UpsertObservation(Observation(Start, Start.AddMinutes(2), send: 400, receive: 500));
        history.Reclassify(new GameEndpointClassifier());

        // Новое наблюдение обновляет факты, но не стирает прежнюю классификацию.
        history.UpsertObservation(Observation(Start.AddMinutes(3), Start.AddMinutes(4), send: 600, receive: 700));
        var afterObservation = history.Records().Single();
        afterObservation.Classification.Should().NotBeNull();
        afterObservation.SendCount.Should().Be(600);

        history.Reclassify(new GameEndpointClassifier());

        var updated = history.Records().Single();
        updated.Classification!.Category.Should().Be(GameEndpointCategory.LikelyGameRelay);
        updated.SendCount.Should().Be(600);
        updated.ReceiveCount.Should().Be(700);
    }

    [Fact]
    public void HistoryDoesNotStorePayloadOrSecrets()
    {
        var history = new GameEndpointHistory(_path);
        history.UpsertObservation(Observation(Start, Start.AddSeconds(10)));
        history.Save();

        var json = File.ReadAllText(_path);
        json.Should().NotContain("PrivateKey");
        json.Should().NotContain("payload");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-23T12:00:00Z");

    private static GameEndpointObservation Observation(
        DateTimeOffset firstSeen,
        DateTimeOffset lastSeen,
        long send = 10,
        long receive = 10,
        int remotePort = 27024) =>
        new()
        {
            RemoteAddress = IPAddress.Parse("203.0.113.9"),
            RemotePort = remotePort,
            LocalAddress = IPAddress.Parse("192.168.1.10"),
            LocalPort = 65479,
            Protocol = TransportProtocol.Udp,
            ProcessId = 24568,
            ProcessName = "dota2",
            ProcessPath = @"C:\Games\dota2.exe",
            Source = EndpointDetectionSource.Etw,
            FirstSeen = firstSeen,
            LastSeen = lastSeen,
            SendCount = send,
            ReceiveCount = receive,
            ObservationCount = 4,
            ProcessStartTime = firstSeen.AddSeconds(-1),
            ObservedSizeFieldSum = 1000,
            ObservedDataSizeFieldSum = 0,
        };
}
