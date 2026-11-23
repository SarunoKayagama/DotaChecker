using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotaChecker.Core.Configuration;
using DotaChecker.Core.Warp;
using DotaChecker.Network.Warp;

namespace DotaChecker.App.ViewModels;

public sealed partial class WarpProfilesViewModel : ObservableObject
{
    private readonly AmneziaWgController _controller;
    private readonly WarpscoutService _warpscout;
    private readonly ISettingsManager _settings;
    private CancellationTokenSource? _scanCancellation;

    public WarpProfilesViewModel(
        AmneziaWgController controller,
        WarpscoutService warpscout,
        ISettingsManager settings)
    {
        _controller = controller;
        _warpscout = warpscout;
        _settings = settings;
        RefreshProfiles();
    }

    public ObservableCollection<WarpProfileRowViewModel> Profiles { get; } = new();

    public ObservableCollection<WarpscoutEndpoint> WarpscoutEndpoints { get; } = new();

    [ObservableProperty]
    private string _statusMessage = "WARPSCOUT — предварительный отбор, не оценка качества для Dota 2.";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private WarpProfileRowViewModel? _selectedProfile;

    [ObservableProperty]
    private string _warpscoutPath = string.Empty;

    [ObservableProperty]
    private string _configDirectory = string.Empty;

    public string ComponentsText =>
        $"AmneziaWG components: {(_controller.ComponentsPresent ? "найдены" : "не найдены")}; " +
        $"elevated: {AmneziaWgController.IsElevated}";

    public void LoadSettings()
    {
        WarpscoutPath = _settings.Settings.WarpscoutBinaryPath;
        ConfigDirectory = _settings.Settings.AmneziaWgConfigDirectory;
        RefreshProfiles();
    }

    partial void OnWarpscoutPathChanged(string value)
    {
        _settings.Settings.WarpscoutBinaryPath = value;
        _settings.Save();
    }

    partial void OnConfigDirectoryChanged(string value)
    {
        _settings.Settings.AmneziaWgConfigDirectory = value;
        _settings.Save();
        RefreshProfiles();
    }

    [RelayCommand]
    private void RefreshProfiles()
    {
        Profiles.Clear();
        foreach (var profile in _controller.FindProfiles(_settings.Settings.AmneziaWgConfigDirectory))
        {
            Profiles.Add(new WarpProfileRowViewModel(profile));
        }

        StatusMessage = $"Найдено профилей: {Profiles.Count}. Секреты не отображаются.";
        OnPropertyChanged(nameof(ComponentsText));
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        if (!File.Exists(_settings.Settings.WarpscoutBinaryPath))
        {
            StatusMessage = "warpscout.exe не найден. Укажите путь в настройках.";
            return;
        }

        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        IsScanning = true;
        StatusMessage = "Сканирование WARPSCOUT... (можно отменить)";

        try
        {
            var result = await _warpscout.ScanAsync(cancellationToken: _scanCancellation.Token);
            WarpscoutEndpoints.Clear();
            foreach (var endpoint in result.Endpoints)
            {
                WarpscoutEndpoints.Add(endpoint);
            }

            var message = $"WARPSCOUT: {result.Status}, версия {result.BinaryVersion}, " +
                          $"endpoints {result.Endpoints.Count}. " +
                          (File.Exists(result.ConfigPath) ? $"Конфиг сохранён: {result.ConfigPath}. " : string.Empty) +
                          result.ErrorMessage;
            RefreshProfiles();
            StatusMessage = message;
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _scanCancellation?.Cancel();
        StatusMessage = "Отмена сканирования запрошена.";
    }

    [RelayCommand]
    private async Task CheckTunnel(WarpProfileRowViewModel? row)
    {
        row ??= Profiles.FirstOrDefault();
        if (row is null)
        {
            return;
        }

        var status = await _controller.GetTunnelStatusAsync(row.Name);
        row.SetTunnel(status);
        StatusMessage = $"{row.Name}: {status.State}. {status.Error}";
    }

    [RelayCommand]
    private void CopyWarnings(WarpProfileRowViewModel? row)
    {
        row ??= Profiles.FirstOrDefault();
        if (row is null)
        {
            return;
        }

        Clipboard.SetText(row.Warnings);
        StatusMessage = "Предупреждения скопированы (без секретов).";
    }
}
