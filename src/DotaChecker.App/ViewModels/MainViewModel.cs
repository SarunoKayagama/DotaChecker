using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotaChecker.App.Services;
using DotaChecker.Core.Configuration;

namespace DotaChecker.App.ViewModels;

/// <summary>ViewModel главного окна: вкладки, тема и настройки.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ISettingsManager _settingsManager;

    public MainViewModel(
        ISettingsManager settingsManager,
        ConnectionsViewModel connections,
        RouteMonitorViewModel routeMonitor,
        GameServersViewModel gameServers,
        WarpProfilesViewModel warpProfiles,
        RouteOptimizationViewModel routeOptimization,
        IDiagnosticsLauncher launcher)
    {
        _settingsManager = settingsManager;
        Connections = connections;
        RouteMonitor = routeMonitor;
        GameServers = gameServers;
        WarpProfiles = warpProfiles;
        RouteOptimization = routeOptimization;

        launcher.RequestRouteMonitorTab += () => SelectedTabIndex = 1;
        launcher.StatusMessage += message => StatusText = message;
    }

    public ConnectionsViewModel Connections { get; }

    public RouteMonitorViewModel RouteMonitor { get; }

    public GameServersViewModel GameServers { get; }

    public WarpProfilesViewModel WarpProfiles { get; }

    public RouteOptimizationViewModel RouteOptimization { get; }

    public AppSettings Settings => _settingsManager.Settings;

    public string SettingsPath => _settingsManager.SettingsPath;

    public static Array AvailableThemes => Enum.GetValues<ThemeMode>();

    [ObservableProperty]
    private string _statusText = "Готово.";

    [ObservableProperty]
    private int _selectedTabIndex;

    public ThemeMode SelectedTheme
    {
        get => Settings.Theme;
        set
        {
            if (Settings.Theme == value)
            {
                return;
            }

            Settings.Theme = value;
            Services.ThemeService.Apply(value);
            OnPropertyChanged();
            _settingsManager.Save();
        }
    }

    public int ProbeIntervalMs
    {
        get => Settings.ProbeIntervalMs;
        set => SetSetting(s => s.ProbeIntervalMs = Math.Clamp(value, 100, 60_000));
    }

    public int ProbeTimeoutMs
    {
        get => Settings.ProbeTimeoutMs;
        set => SetSetting(s => s.ProbeTimeoutMs = Math.Clamp(value, 100, 60_000));
    }

    public int TcpProbeTimeoutMs
    {
        get => Settings.TcpProbeTimeoutMs;
        set => SetSetting(s => s.TcpProbeTimeoutMs = Math.Clamp(value, 3000, 60_000));
    }

    public int MaxConcurrentProbes
    {
        get => Settings.MaxConcurrentProbes;
        set => SetSetting(s => s.MaxConcurrentProbes = Math.Clamp(value, 1, 256));
    }

    public int RouteRefreshSeconds
    {
        get => Settings.RouteRefreshSeconds;
        set => SetSetting(s => s.RouteRefreshSeconds = Math.Clamp(value, 10, 3600));
    }

    public int MaxHops
    {
        get => Settings.MaxHops;
        set => SetSetting(s => s.MaxHops = Math.Clamp(value, 1, 64));
    }

    public int MinSamplesForDiagnosis
    {
        get => Settings.MinSamplesForDiagnosis;
        set => SetSetting(s => s.MinSamplesForDiagnosis = Math.Clamp(value, 5, 1000));
    }

    public double LossThresholdPct
    {
        get => Settings.LossThresholdPct;
        set => SetSetting(s => s.LossThresholdPct = Math.Clamp(value, 0.1, 100.0));
    }

    public int UdpDestinationPort
    {
        get => Settings.UdpDestinationPort;
        set => SetSetting(s => s.UdpDestinationPort = Math.Clamp(value, 1, 65535));
    }

    public int TcpDestinationPort
    {
        get => Settings.TcpDestinationPort;
        set => SetSetting(s => s.TcpDestinationPort = Math.Clamp(value, 1, 65535));
    }

    public bool ReverseDnsEnabled
    {
        get => Settings.ReverseDnsEnabled;
        set => SetSetting(s => s.ReverseDnsEnabled = value);
    }

    public string WarpscoutBinaryPath
    {
        get => Settings.WarpscoutBinaryPath;
        set => SetSetting(s => s.WarpscoutBinaryPath = value ?? string.Empty);
    }

    public int WarpscoutTimeoutSeconds
    {
        get => Settings.WarpscoutTimeoutSeconds;
        set => SetSetting(s => s.WarpscoutTimeoutSeconds = Math.Clamp(value, 30, 3600));
    }

    public string AmneziaWgConfigDirectory
    {
        get => Settings.AmneziaWgConfigDirectory;
        set => SetSetting(s => s.AmneziaWgConfigDirectory = value ?? string.Empty);
    }

    [RelayCommand]
    private void ResetSettings()
    {
        _settingsManager.Reset();
        Services.ThemeService.Apply(Settings.Theme);

        OnPropertyChanged(nameof(SelectedTheme));
        OnPropertyChanged(nameof(ProbeIntervalMs));
        OnPropertyChanged(nameof(ProbeTimeoutMs));
        OnPropertyChanged(nameof(TcpProbeTimeoutMs));
        OnPropertyChanged(nameof(MaxConcurrentProbes));
        OnPropertyChanged(nameof(RouteRefreshSeconds));
        OnPropertyChanged(nameof(MaxHops));
        OnPropertyChanged(nameof(MinSamplesForDiagnosis));
        OnPropertyChanged(nameof(LossThresholdPct));
        OnPropertyChanged(nameof(UdpDestinationPort));
        OnPropertyChanged(nameof(TcpDestinationPort));
        OnPropertyChanged(nameof(ReverseDnsEnabled));
        OnPropertyChanged(nameof(WarpscoutBinaryPath));
        OnPropertyChanged(nameof(WarpscoutTimeoutSeconds));
        OnPropertyChanged(nameof(AmneziaWgConfigDirectory));
    }

    private void SetSetting(Action<AppSettings> apply, [CallerMemberName] string? propertyName = null)
    {
        apply(Settings);
        OnPropertyChanged(propertyName);
        _settingsManager.Save();
    }
}
