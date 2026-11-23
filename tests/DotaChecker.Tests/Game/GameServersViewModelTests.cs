using System.Net;
using System.Windows.Data;
using DotaChecker.App.Services;
using DotaChecker.App.ViewModels;
using DotaChecker.Core.Game;
using DotaChecker.Core.Models;
using DotaChecker.Network.Discovery;
using DotaChecker.Network.Discovery.Models;
using DotaChecker.Network.Monitoring;

namespace DotaChecker.Tests.Game;

public sealed class GameServersViewModelTests
{
    [Fact]
    public void UpdatedEvent_AddsRowWithoutDuplicates()
    {
        var service = new FakeDiscoveryService();
        var launcher = new FakeLauncher();
        var viewModel = new GameServersViewModel(service, new FakeMonitoring(), launcher);

        var record = Record(27015);
        service.RaiseUpdated(record);
        service.RaiseUpdated(record);

        viewModel.Rows.Should().ContainSingle();
        viewModel.Rows[0].RemoteEndPoint.Should().Be("203.0.113.9:27015");
    }

    [Fact]
    public void StartAndStop_UpdateStatusFromService()
    {
        var service = new FakeDiscoveryService();
        var viewModel = new GameServersViewModel(service, new FakeMonitoring(), new FakeLauncher());

        viewModel.StartDiscoveryCommand.Execute(null);

        viewModel.IsDiscoveryRunning.Should().BeTrue();
        viewModel.EtwStatusText.Should().Contain("Available");
        service.StartCount.Should().Be(1);

        viewModel.StopDiscoveryCommand.Execute(null);

        viewModel.IsDiscoveryRunning.Should().BeFalse();
        service.StopCount.Should().Be(1);
    }

    [Fact]
    public void CategoryAndTextFilters_ApplyToDefaultView()
    {
        var service = new FakeDiscoveryService();
        var viewModel = new GameServersViewModel(service, new FakeMonitoring(), new FakeLauncher());
        service.RaiseUpdated(Record(27015, GameEndpointCategory.LikelyGameRelay));
        service.RaiseUpdated(Record(443, GameEndpointCategory.Unknown));
        var view = CollectionViewSource.GetDefaultView(viewModel.Rows);

        view.Cast<object>().Should().HaveCount(2);

        viewModel.SelectedCategory = "Unknown";
        view.Cast<object>().Should().ContainSingle();

        viewModel.SelectedCategory = "All";
        viewModel.FilterText = "443";
        view.Cast<object>().Should().ContainSingle();
    }

    [Fact]
    public void IncludeExcludeCommands_CallService()
    {
        var service = new FakeDiscoveryService();
        var viewModel = new GameServersViewModel(service, new FakeMonitoring(), new FakeLauncher());
        var record = Record(27015);
        service.RaiseUpdated(record);
        viewModel.SelectedRow = viewModel.Rows[0];

        viewModel.ExcludeEndpointCommand.Execute(null);
        service.LastExcludedKey.Should().Be(record.Key);

        viewModel.IncludeEndpointCommand.Execute(null);
        service.LastIncludedKey.Should().Be(record.Key);
    }

    [Fact]
    public void ContinuousPing_UsesExistingLauncherAndWarnsAboutUdp()
    {
        var service = new FakeDiscoveryService();
        var launcher = new FakeLauncher();
        var viewModel = new GameServersViewModel(service, new FakeMonitoring(), launcher);
        service.RaiseUpdated(Record(27015));
        viewModel.SelectedRow = viewModel.Rows[0];

        viewModel.ContinuousPingCommand.Execute(null);

        launcher.LastTarget.Should().Be(IPAddress.Parse("203.0.113.9"));
        viewModel.StatusMessage.Should().Contain("НЕ измерение потерь игрового UDP-трафика");
    }

    [Fact]
    public void DetailsContainHypothesisAndSeparateSizeFields()
    {
        var service = new FakeDiscoveryService();
        var viewModel = new GameServersViewModel(service, new FakeMonitoring(), new FakeLauncher());
        var record = Record(27015, GameEndpointCategory.LikelyGameRelay);
        service.RaiseUpdated(record);

        var details = viewModel.Rows[0].Details;

        details.Should().Contain("size field");
        details.Should().Contain("dsize field");
        details.Should().Contain("История классификаций");
        viewModel.Rows[0].CategoryText.Should().Contain("гипотеза");
    }

    private static GameEndpointRecord Record(int remotePort, GameEndpointCategory category = GameEndpointCategory.Unknown) =>
        new()
        {
            Key = GameEndpointKeys.Build(24568, TransportProtocol.Udp,
                IPAddress.Parse("203.0.113.9"), remotePort, IPAddress.Parse("192.168.1.10"), 65479),
            RemoteAddress = IPAddress.Parse("203.0.113.9"),
            RemotePort = remotePort,
            LocalAddress = IPAddress.Parse("192.168.1.10"),
            LocalPort = 65479,
            Protocol = TransportProtocol.Udp,
            ProcessId = 24568,
            ProcessName = "dota2",
            Source = EndpointDetectionSource.Etw,
            FirstSeen = DateTimeOffset.Parse("2026-11-23T12:00:00Z"),
            LastSeen = DateTimeOffset.Parse("2026-11-23T12:02:00Z"),
            SendCount = 100,
            ReceiveCount = 200,
            ObservationCount = 4,
            ObservedSizeFieldSum = 1000,
            ObservedDataSizeFieldSum = 0,
            Classification = new GameEndpointClassification(
                category,
                EvidenceLevel.Medium,
                new[] { "Fact: bidirectional UDP traffic" },
                DateTimeOffset.UtcNow,
                GameEndpointClassifier.Version),
        };

    private sealed class FakeDiscoveryService : IGameEndpointDiscoveryService
    {
        private readonly List<GameEndpointRecord> _records = new();

        public event Action<IReadOnlyList<GameEndpointRecord>>? Updated;

        public EtAvailability Status { get; private set; } = EtAvailability.Unknown;
        public string StatusReason => "fake";
        public bool IsRunning { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public string? LastExcludedKey { get; private set; }
        public string? LastIncludedKey { get; private set; }

        public Task StartAsync()
        {
            StartCount++;
            IsRunning = true;
            Status = EtAvailability.Available;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCount++;
            IsRunning = false;
            return Task.CompletedTask;
        }

        public IReadOnlyList<GameEndpointRecord> Records() => _records.ToArray();

        public bool Exclude(string key)
        {
            LastExcludedKey = key;
            return true;
        }

        public bool Include(string key)
        {
            LastIncludedKey = key;
            return true;
        }

        public void Save()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void RaiseUpdated(params GameEndpointRecord[] records)
        {
            foreach (var record in records)
            {
                var index = _records.FindIndex(item => item.Key == record.Key);
                if (index >= 0)
                {
                    _records[index] = record;
                }
                else
                {
                    _records.Add(record);
                }
            }

            Updated?.Invoke(_records.ToArray());
        }
    }

    private sealed class FakeMonitoring : IGameEndpointMonitoringService
    {
        public event Action<IReadOnlyList<EndpointMonitoringSummary>>? Updated;

        public string Status => "не запущен";
        public bool IsRunning => false;
        public EndpointMonitoringSummary? Current => null;

        public IReadOnlyList<EndpointMonitoringSummary> Summaries()
        {
            Updated?.Invoke(Array.Empty<EndpointMonitoringSummary>());
            return Array.Empty<EndpointMonitoringSummary>();
        }

        public Task StartAsync(GameEndpointRecord endpoint) => Task.CompletedTask;

        public Task StopAsync(string? reason = null) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeLauncher : IDiagnosticsLauncher
    {
        public event Action<string>? StatusMessage;
        public event Action? RequestRouteMonitorTab;

        public IPAddress? LastTarget { get; private set; }

        public Task PingOnceAsync(IPAddress target)
        {
            StatusMessage?.Invoke("ping");
            return Task.CompletedTask;
        }

        public Task ContinuousPingAsync(IPAddress target)
        {
            LastTarget = target;
            RequestRouteMonitorTab?.Invoke();
            return Task.CompletedTask;
        }

        public Task TracerouteOnceAsync(IPAddress target) => Task.CompletedTask;

        public Task MonitorRouteAsync(IPAddress target) => Task.CompletedTask;

        public Task ReverseDnsAsync(IPAddress target) => Task.CompletedTask;
    }
}
