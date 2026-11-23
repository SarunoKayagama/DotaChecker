using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotaChecker.App.Services;
using DotaChecker.Core.Game;
using DotaChecker.Network.Discovery;
using DotaChecker.Network.Discovery.Models;
using DotaChecker.Network.Monitoring;

namespace DotaChecker.App.ViewModels;

public sealed partial class GameServersViewModel : ObservableObject
{
    private readonly IGameEndpointDiscoveryService _service;
    private readonly IGameEndpointMonitoringService _monitoring;
    private readonly IDiagnosticsLauncher _launcher;
    private readonly ICollectionView _view;
    private string _filterText = string.Empty;

    public GameServersViewModel(
        IGameEndpointDiscoveryService service,
        IGameEndpointMonitoringService monitoring,
        IDiagnosticsLauncher launcher)
    {
        _service = service;
        _monitoring = monitoring;
        _launcher = launcher;
        _view = CollectionViewSource.GetDefaultView(Rows);
        _view.Filter = RowMatchesFilter;
        _service.Updated += OnUpdated;
        _monitoring.Updated += OnMonitoringUpdated;
        RefreshFromService();
    }

    public ObservableCollection<GameEndpointRowViewModel> Rows { get; } = new();

    public static string[] CategoryFilters =>
        new[] { "All" }.Concat(Enum.GetNames<GameEndpointCategory>()).ToArray();

    public string EtwStatusText =>
        $"{_service.Status}: {_service.StatusReason}";

    [ObservableProperty]
    private bool _isDiscoveryRunning;

    [ObservableProperty]
    private string _statusMessage = "Endpoint считается гипотезой, а не подтверждённым игровым сервером.";

    [ObservableProperty]
    private GameEndpointRowViewModel? _selectedRow;

    [ObservableProperty]
    private string _selectedCategory = "All";

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                _view.Refresh();
            }
        }
    }

    [RelayCommand]
    private async Task StartDiscovery()
    {
        await _service.StartAsync();
        IsDiscoveryRunning = _service.IsRunning;
        StatusMessage = $"ETW: {_service.Status}. {_service.StatusReason}";
        OnPropertyChanged(nameof(EtwStatusText));
    }

    [RelayCommand]
    private async Task StopDiscovery()
    {
        await _service.StopAsync();
        IsDiscoveryRunning = false;
        StatusMessage = "ETW-наблюдение остановлено. История сохранена.";
        OnPropertyChanged(nameof(EtwStatusText));
    }

    [RelayCommand]
    private void CopyIp(GameEndpointRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null)
        {
            return;
        }

        Clipboard.SetText(row.RemoteIp);
        StatusMessage = $"Скопировано: {row.RemoteIp}";
    }

    [RelayCommand]
    private void CopyEndpoint(GameEndpointRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null)
        {
            return;
        }

        Clipboard.SetText(row.RemoteEndPoint);
        StatusMessage = $"Скопировано: {row.RemoteEndPoint}";
    }

    [RelayCommand]
    private void IncludeEndpoint(GameEndpointRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is not null && _service.Include(row.Key))
        {
            StatusMessage = $"Endpoint включён вручную: {row.RemoteEndPoint}";
        }
    }

    [RelayCommand]
    private void ExcludeEndpoint(GameEndpointRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is not null && _service.Exclude(row.Key))
        {
            StatusMessage = $"Endpoint исключён вручную: {row.RemoteEndPoint}";
        }
    }

    [RelayCommand]
    private async Task StartMonitoring(GameEndpointRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null)
        {
            return;
        }

        var record = _service.Records().FirstOrDefault(item => item.Key == row.Key);
        if (record is null)
        {
            StatusMessage = "Endpoint отсутствует в истории discovery.";
            return;
        }

        await _monitoring.StartAsync(record);
        StatusMessage = _monitoring.Status;
    }

    [RelayCommand]
    private async Task StopMonitoring()
    {
        await _monitoring.StopAsync();
        StatusMessage = _monitoring.Status;
    }

    [RelayCommand]
    private async Task ContinuousPing(GameEndpointRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null)
        {
            return;
        }

        await _launcher.ContinuousPingAsync(System.Net.IPAddress.Parse(row.RemoteIp));
        StatusMessage = "Запущен Continuous Ping (ICMP). Это НЕ измерение потерь игрового UDP-трафика.";
    }

    partial void OnSelectedCategoryChanged(string value) => _view.Refresh();

    private bool RowMatchesFilter(object item)
    {
        if (item is not GameEndpointRowViewModel row)
        {
            return false;
        }

        if (SelectedCategory != "All" && row.Category.ToString() != SelectedCategory)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(FilterText))
        {
            return true;
        }

        var needle = FilterText.Trim();
        return row.RemoteIp.Contains(needle, StringComparison.OrdinalIgnoreCase)
               || row.RemoteEndPoint.Contains(needle, StringComparison.OrdinalIgnoreCase)
               || row.LocalEndPoint.Contains(needle, StringComparison.OrdinalIgnoreCase)
               || row.ProcessName.Contains(needle, StringComparison.OrdinalIgnoreCase)
               || row.ProcessId.ToString().Contains(needle, StringComparison.Ordinal);
    }

    private void OnUpdated(IReadOnlyList<GameEndpointRecord> records) =>
        RunOnUi(() =>
        {
            var existing = Rows.ToDictionary(row => row.Key, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var record in records)
            {
                seen.Add(record.Key);
                if (existing.TryGetValue(record.Key, out var row))
                {
                    row.Update(record);
                }
                else
                {
                    Rows.Add(new GameEndpointRowViewModel(record));
                }
            }

            for (var i = Rows.Count - 1; i >= 0; i--)
            {
                if (!seen.Contains(Rows[i].Key))
                {
                    Rows.RemoveAt(i);
                }
            }

            _view.Refresh();
            OnPropertyChanged(nameof(EtwStatusText));
        });

    private void OnMonitoringUpdated(IReadOnlyList<EndpointMonitoringSummary> summaries) =>
        RunOnUi(() =>
        {
            foreach (var row in Rows)
            {
                row.SetMonitoring(summaries.FirstOrDefault(item => item.EndpointKey == row.Key));
            }
        });

    private void RefreshFromService()
    {
        OnUpdated(_service.Records());
        OnMonitoringUpdated(_monitoring.Summaries());
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}
