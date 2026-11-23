using System.Net;
using DotaChecker.Core.Configuration;
using DotaChecker.Core.Diagnostics;
using DotaChecker.Core.Models;
using DotaChecker.Core.Statistics;

namespace DotaChecker.Network.Monitoring;

/// <summary>
/// Непрерывный ICMP-мониторинг. Каждый раунд опрашивает все известные узлы
/// параллельно (с ограничением конкуренции) и отдельно отправляет e2e-пробу.
/// E2e — дополнительный сигнал, а не источник истины.
/// </summary>
public sealed class ContinuousProbeEngine : IAsyncDisposable
{
    private const int E2eSlot = 900;
    private const int DiscoverySlotBase = 950;
    private const int PublishIntervalMs = 700;
    private const int MaxStoredVersions = 20;
    private const int MaxStoredEvents = 200;

    private static readonly HopStatisticsSnapshot EmptyStatistics = new HopStatistics().Snapshot();

    private readonly IProbeTransport _transport;
    private readonly IRouteDiscovery _discovery;
    private readonly StatisticsEngine _statistics;
    private readonly AppSettings _settings;
    private readonly Dns.DnsResolver _dnsResolver;
    private readonly SemaphoreSlim _sendGate;
    private readonly RouteStabilityTracker _stability = new();
    private readonly Dictionary<IPAddress, string> _hostnames = new();
    private readonly object _hostnamesGate = new();
    private readonly List<RouteVersion> _versions = new();
    private readonly List<RouteChangeEvent> _events = new();

    private CancellationTokenSource? _cancellation;
    private Task? _runTask;
    private RouteSnapshot? _route;
    private RouteVersion? _activeVersion;
    private int _versionId;
    private int _round;

    public ContinuousProbeEngine(
        IProbeTransport transport,
        IRouteDiscovery discovery,
        StatisticsEngine statistics,
        AppSettings settings,
        Dns.DnsResolver dnsResolver)
    {
        _transport = transport;
        _discovery = discovery;
        _statistics = statistics;
        _settings = settings;
        _dnsResolver = dnsResolver;
        _sendGate = new SemaphoreSlim(Math.Max(1, settings.MaxConcurrentProbes));
    }

    public event Action<MonitorSnapshot>? SnapshotPublished;

    public event Action<IReadOnlyList<RouteChangeEvent>>? RouteChanged;

    public event Action<string>? ErrorOccurred;

    public bool IsRunning => _cancellation is not null;

    public IPAddress? Target { get; private set; }

    public RouteSnapshot? CurrentRoute => Volatile.Read(ref _route);

    public IReadOnlyList<RouteVersion> Versions
    {
        get
        {
            lock (_events)
            {
                return _versions.ToArray();
            }
        }
    }

    public IReadOnlyList<RouteChangeEvent> Events
    {
        get
        {
            lock (_events)
            {
                return _events.ToArray();
            }
        }
    }

    public int Round => Volatile.Read(ref _round);

    public async Task StartAsync(IPAddress target, CancellationToken cancellationToken = default)
    {
        var route = await _discovery.DiscoverAsync(
            target,
            Math.Max(1, _settings.MaxHops),
            TimeSpan.FromMilliseconds(Math.Max(100, _settings.ProbeTimeoutMs)),
            cancellationToken).ConfigureAwait(false);

        await StartWithRouteAsync(route, cancellationToken).ConfigureAwait(false);
    }

    public Task StartWithRouteAsync(RouteSnapshot route, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Мониторинг уже запущен.");
        }

        Target = route.Target;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Volatile.Write(ref _route, route);
        _stability.OnDiscovery(route);

        lock (_events)
        {
            _versions.Clear();
            _events.Clear();
            _versionId = 1;
            _activeVersion = new RouteVersion(_versionId, DateTimeOffset.UtcNow, route, true);
            _versions.Add(_activeVersion);
        }

        _statistics.BeginNewVersion(_activeVersion);
        Volatile.Write(ref _round, 0);
        _ = ResolveHostnamesAsync(route, _cancellation.Token);
        _runTask = RunAsync(_cancellation.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var cancellation = _cancellation;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        try
        {
            if (_runTask is not null)
            {
                await _runTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Нормальная остановка.
        }
        catch (Exception exception)
        {
            ErrorOccurred?.Invoke(exception.Message);
        }

        cancellation.Dispose();
        _cancellation = null;
        _runTask = null;
    }

    public void ResetStatistics() => _statistics.ResetAll();

    /// <summary>Текущие статистические снимки по-hop (для внешнего мониторинга endpoint).</summary>
    public IReadOnlyDictionary<int, HopStatisticsSnapshot> Statistics() => _statistics.CurrentSnapshots();

    public HopStatisticsSnapshot E2eStatistics() => _statistics.E2eSnapshot();

    /// <summary>Снимок данных для RouteAnalyzer. На входе — только измерения.</summary>
    public AnalysisInput CreateAnalysisInput(bool controlledExperiment = false)
    {
        var route = Volatile.Read(ref _route)
            ?? throw new InvalidOperationException("Маршрут ещё не обнаружен.");
        return new AnalysisInput(_statistics.Windows(), route, ProbeProtocol.Icmp, controlledExperiment);
    }

    /// <summary>Задержка обнаружения настоящей смены маршрута, в discovery.</summary>
    public int RouteChangeDetectionLatency => _stability.CommittedLatencyDiscoveries;

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _sendGate.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var probeTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(100, _settings.ProbeIntervalMs)));
        using var routeTimer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(10, _settings.RouteRefreshSeconds)));
        using var publishTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(PublishIntervalMs));

        await Task.WhenAll(
                RoundLoopAsync(probeTimer, cancellationToken),
                RouteLoopAsync(routeTimer, cancellationToken),
                PublishLoopAsync(publishTimer, cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task RoundLoopAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromMilliseconds(Math.Max(100, _settings.ProbeTimeoutMs));

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            Interlocked.Increment(ref _round);
            var route = Volatile.Read(ref _route);
            if (route is null || Target is null)
            {
                continue;
            }

            var tasks = new List<Task>(route.Hops.Count + 1);
            foreach (var hop in route.Hops)
            {
                tasks.Add(ProbeHopAsync(hop.HopIndex, Target, timeout, cancellationToken));
            }

            tasks.Add(ProbeE2eAsync(Target, timeout, cancellationToken));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    private async Task ProbeHopAsync(int hopIndex, IPAddress target, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var reply = await _transport.SendAsync(
                        new ProbeRequest(hopIndex, target, hopIndex, timeout),
                        cancellationToken)
                    .ConfigureAwait(false);
                _statistics.RecordHop(hopIndex, reply);
            }
            finally
            {
                _sendGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Остановка сессии.
        }
        catch (Exception exception)
        {
            _statistics.RecordHop(hopIndex, new ProbeReply(target, ProbeOutcome.Error, 0, exception.Message));
        }
    }

    private async Task ProbeE2eAsync(IPAddress target, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // TTL 255: максимальный TTL IPv4. Не гарантирует ответ (фильтрация),
                // но убирает жёсткое ограничение длины маршрута.
                var reply = await _transport.SendAsync(
                        new ProbeRequest(E2eSlot, target, 255, timeout),
                        cancellationToken)
                    .ConfigureAwait(false);
                _statistics.RecordE2e(reply);
            }
            finally
            {
                _sendGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Остановка сессии.
        }
        catch (Exception exception)
        {
            _statistics.RecordE2e(new ProbeReply(target, ProbeOutcome.Error, 0, exception.Message));
        }
    }

    private async Task RouteLoopAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromMilliseconds(Math.Min(1500, Math.Max(100, _settings.ProbeTimeoutMs)));

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Target is null)
            {
                continue;
            }

            try
            {
                var route = await _discovery.DiscoverAsync(
                        Target,
                        Math.Max(1, _settings.MaxHops),
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
                ApplyRoute(route, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                ErrorOccurred?.Invoke($"Повторная трассировка: {exception.Message}");
            }
        }
    }

    private void ApplyRoute(RouteSnapshot newRoute, CancellationToken cancellationToken)
    {
        var oldRoute = Volatile.Read(ref _route);
        if (oldRoute is null)
        {
            return;
        }

        var stability = _stability.OnDiscovery(newRoute);
        if (stability.Status is RouteStabilityStatus.NoChange
            or RouteStabilityStatus.Initialized
            or RouteStabilityStatus.Pending)
        {
            return;
        }

        var isCommitted = stability.Status == RouteStabilityStatus.Committed;
        var description = isCommitted
            ? $"Новая версия маршрута подтверждена за {stability.CommittedLatencyDiscoveries} discovery. {RouteComparer.Compare(oldRoute, newRoute).Description}"
            : $"ECMP-вариация: {RouteComparer.Compare(oldRoute, newRoute).Description}";
        RouteChangeEvent changeEvent;

        lock (_events)
        {
            if (isCommitted)
            {
                if (_activeVersion is not null)
                {
                    _activeVersion.IsActive = false;
                }

                _versionId++;
                _activeVersion = new RouteVersion(_versionId, DateTimeOffset.UtcNow, newRoute, true);
                _versions.Add(_activeVersion);
                while (_versions.Count > MaxStoredVersions)
                {
                    _versions.RemoveAt(0);
                }

                _statistics.BeginNewVersion(_activeVersion);
            }

            changeEvent = new RouteChangeEvent(
                DateTimeOffset.UtcNow,
                isCommitted ? RouteChangeKind.RouteReplaced : RouteChangeKind.EcmpVariation,
                description);
            _events.Add(changeEvent);
            while (_events.Count > MaxStoredEvents)
            {
                _events.RemoveAt(0);
            }
        }

        Volatile.Write(ref _route, newRoute);
        _ = ResolveHostnamesAsync(newRoute, cancellationToken);
        RouteChanged?.Invoke(new[] { changeEvent });
    }

    private async Task PublishLoopAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            _statistics.RotateWindows(DateTimeOffset.UtcNow);

            var route = Volatile.Read(ref _route);
            if (route is null)
            {
                continue;
            }

            var hopStats = _statistics.CurrentSnapshots();
            var hops = new List<MonitorHopView>(route.Hops.Count);

            foreach (var hop in route.Hops)
            {
                var hostname = GetHostname(hop.PrimaryAddress);
                hops.Add(new MonitorHopView(
                    hop.HopIndex,
                    hop.Responders,
                    hostname,
                    hop.IsDestination,
                    hopStats.TryGetValue(hop.HopIndex, out var stats) ? stats : EmptyStatistics));
            }

            var e2e = new MonitorHopView(
                0,
                new[] { route.Target },
                GetHostname(route.Target),
                true,
                _statistics.E2eSnapshot());

            SnapshotPublished?.Invoke(new MonitorSnapshot(
                route.Target,
                Volatile.Read(ref _round),
                hops,
                e2e,
                DateTimeOffset.UtcNow));
        }
    }

    private string GetHostname(IPAddress? address)
    {
        if (address is null)
        {
            return "—";
        }

        lock (_hostnamesGate)
        {
            return _hostnames.TryGetValue(address, out var name) ? name : address.ToString();
        }
    }

    private async Task ResolveHostnamesAsync(RouteSnapshot route, CancellationToken cancellationToken)
    {
        if (!_settings.ReverseDnsEnabled)
        {
            return;
        }

        foreach (var address in route.Hops
                     .SelectMany(hop => hop.Responders)
                     .Append(route.Target)
                     .Distinct())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var name = await _dnsResolver.ResolveAsync(address, cancellationToken).ConfigureAwait(false);
                lock (_hostnamesGate)
                {
                    _hostnames[address] = name;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                ErrorOccurred?.Invoke($"Reverse DNS {address}: {exception.Message}");
            }
        }
    }
}
