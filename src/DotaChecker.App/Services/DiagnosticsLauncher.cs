using System.Net;
using DotaChecker.Core.Configuration;
using DotaChecker.Core.Diagnostics;
using DotaChecker.Core.Models;
using DotaChecker.Network.Dns;
using DotaChecker.Network.Monitoring;

namespace DotaChecker.App.Services;

/// <summary>
/// Реализация пунктов контекстного меню. В текущих итерациях доступны только ICMP-режимы;
/// пункты UDP/TCP намеренно не реализованы до отдельной проверки Windows API.
/// </summary>
public sealed class DiagnosticsLauncher : IDiagnosticsLauncher
{
    private readonly IProbeTransport _transport;
    private readonly IRouteDiscovery _discovery;
    private readonly ContinuousProbeEngine _engine;
    private readonly DnsResolver _dnsResolver;
    private readonly AppSettings _settings;

    public DiagnosticsLauncher(
        IProbeTransport transport,
        IRouteDiscovery discovery,
        ContinuousProbeEngine engine,
        DnsResolver dnsResolver,
        AppSettings settings)
    {
        _transport = transport;
        _discovery = discovery;
        _engine = engine;
        _dnsResolver = dnsResolver;
        _settings = settings;
    }

    public event Action<string>? StatusMessage;

    public event Action? RequestRouteMonitorTab;

    public async Task PingOnceAsync(IPAddress target)
    {
        var timeout = TimeSpan.FromMilliseconds(Math.Max(100, _settings.ProbeTimeoutMs));
        var rtts = new List<double>();

        for (var i = 0; i < 4; i++)
        {
            var reply = await _transport.SendAsync(new ProbeRequest(800, target, 255, timeout), CancellationToken.None);
            if (reply.Outcome == ProbeOutcome.EchoReply)
            {
                rtts.Add(reply.RttMs);
            }
        }

        var loss = 100.0 * (4 - rtts.Count) / 4;
        var summary = rtts.Count == 0
            ? $"Ping {target}: нет ICMP-ответов (потеря 100%). Это не доказывает потерю транзитного трафика."
            : $"Ping {target}: loss {loss:F1}%, avg {rtts.Average():F1} мс, min {rtts.Min():F1} мс, max {rtts.Max():F1} мс";
        StatusMessage?.Invoke(summary);
    }

    public async Task ContinuousPingAsync(IPAddress target)
    {
        await EnsureStoppedAsync();
        var route = new RouteSnapshot(
            target,
            new[] { new RouteHop(1, new[] { target }, true) },
            DateTimeOffset.UtcNow);
        await _engine.StartWithRouteAsync(route);
        RequestRouteMonitorTab?.Invoke();
        StatusMessage?.Invoke($"Непрерывный ICMP-ping: {target}");
    }

    public async Task TracerouteOnceAsync(IPAddress target)
    {
        var timeout = TimeSpan.FromMilliseconds(Math.Max(100, _settings.ProbeTimeoutMs));
        var route = await _discovery.DiscoverAsync(target, Math.Max(1, _settings.MaxHops), timeout, CancellationToken.None);
        var lines = route.Hops.Select(hop =>
        {
            var address = hop.PrimaryAddress?.ToString() ?? "no reply";
            return $"{hop.HopIndex,2}: {address}{(hop.IsDestination ? "  [destination]" : string.Empty)}";
        });
        StatusMessage?.Invoke($"Traceroute ICMP → {target}: " + string.Join(" | ", lines));
    }

    public async Task MonitorRouteAsync(IPAddress target)
    {
        await EnsureStoppedAsync();
        await _engine.StartAsync(target);
        RequestRouteMonitorTab?.Invoke();
        StatusMessage?.Invoke($"Мониторинг маршрута запущен: {target}");
    }

    public async Task ReverseDnsAsync(IPAddress target)
    {
        var name = await _dnsResolver.ResolveAsync(target, CancellationToken.None);
        StatusMessage?.Invoke($"Reverse DNS {target}: {name}");
    }

    private async Task EnsureStoppedAsync()
    {
        if (_engine.IsRunning)
        {
            await _engine.StopAsync();
        }
    }
}
