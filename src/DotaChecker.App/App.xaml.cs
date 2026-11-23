using System.IO;
using System.Windows;
using DotaChecker.App.Services;
using DotaChecker.App.ViewModels;
using DotaChecker.Core.Configuration;
using DotaChecker.Core.Diagnostics;
using DotaChecker.Core.Game;
using DotaChecker.Core.Optimization;
using DotaChecker.Core.Statistics;
using DotaChecker.Network.Connections;
using DotaChecker.Network.Dns;
using DotaChecker.Network.Discovery;
using DotaChecker.Network.Icmp;
using DotaChecker.Network.Monitoring;
using DotaChecker.Network.Routing;
using DotaChecker.Network.Warp;
using Microsoft.Extensions.DependencyInjection;

namespace DotaChecker.App;

/// <summary>
/// Точка входа. Создаёт контейнер, применяет тему и открывает главное окно.
/// Выход останавливает мониторинг, сохраняет настройки и освобождает ресурсы.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settingsManager = new SettingsManager();

        var services = new ServiceCollection();
        services.AddSingleton<ISettingsManager>(settingsManager);
        services.AddSingleton(settingsManager.Settings);

        services.AddSingleton<IProbeTransport, IcmpProbeTransport>();
        services.AddSingleton<IRouteDiscovery, IcmpRouteDiscovery>();
        services.AddSingleton<StatisticsEngine>();
        services.AddSingleton(new AnalyzerOptions());
        services.AddSingleton<RouteAnalyzer>();
        services.AddSingleton<DnsResolver>();
        services.AddSingleton<ContinuousProbeEngine>();
        services.AddSingleton<ConnectionMonitor>();
        services.AddSingleton<IDiagnosticsLauncher, DiagnosticsLauncher>();

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var gameEndpointsPath = Path.Combine(appData, "Dota Checker", "game-endpoints.json");
        services.AddSingleton<IGameEndpointDiscoveryService>(_ =>
            new GameEndpointDiscoveryService(gameEndpointsPath));

        var monitoringPath = System.IO.Path.Combine(appData, "Dota Checker", "game-endpoint-monitoring.json");
        services.AddSingleton(new EndpointMonitoringHistory(monitoringPath));
        services.AddSingleton<IGameEndpointMonitoringService, GameEndpointMonitoringService>();
        services.AddSingleton<RouteComparisonService>();
        services.AddSingleton<WarpscoutService>();
        services.AddSingleton<AmneziaWgController>();
        services.AddSingleton<RouteInterfaceResolver>();

        services.AddSingleton<ConnectionsViewModel>();
        services.AddSingleton<RouteMonitorViewModel>();
        services.AddSingleton<GameServersViewModel>();
        services.AddSingleton<WarpProfilesViewModel>();
        services.AddSingleton<RouteOptimizationViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();

        _services = services.BuildServiceProvider();

        Services.ThemeService.Apply(settingsManager.Settings.Theme);

        _services.GetRequiredService<ConnectionsViewModel>().Initialize();
        _services.GetRequiredService<WarpProfilesViewModel>().LoadSettings();

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_services is not null)
        {
            await _services.GetRequiredService<ContinuousProbeEngine>().StopAsync();
            await _services.GetRequiredService<IGameEndpointMonitoringService>().DisposeAsync();
            await _services.GetRequiredService<IGameEndpointDiscoveryService>().StopAsync();
            await _services.GetRequiredService<ConnectionMonitor>().DisposeAsync();
            await _services.GetRequiredService<IProbeTransport>().DisposeAsync();

            _services.GetRequiredService<ISettingsManager>().Save();
            _services.Dispose();
            _services = null;
        }

        base.OnExit(e);
    }
}
