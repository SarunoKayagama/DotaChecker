using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotaChecker.App.Services;
using DotaChecker.Core.Models;
using DotaChecker.Network.Connections;

namespace DotaChecker.App.ViewModels;

public sealed partial class ConnectionsViewModel : ObservableObject, IDisposable
{
    private readonly ConnectionMonitor _monitor;
    private readonly IDiagnosticsLauncher _launcher;
    private readonly ICollectionView _view;
    private string _filterText = string.Empty;

    public ConnectionsViewModel(ConnectionMonitor monitor, IDiagnosticsLauncher launcher)
    {
        _monitor = monitor;
        _launcher = launcher;
        _view = CollectionViewSource.GetDefaultView(Rows);
        _view.Filter = RowMatchesFilter;

        _monitor.Updated += OnMonitorUpdated;
        _monitor.Failed += OnMonitorFailed;
        _launcher.StatusMessage += message => StatusMessage = message;
    }

    public ObservableCollection<ConnectionRowViewModel> Rows { get; } = new();

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

    [ObservableProperty]
    private bool _isAutoRefreshEnabled = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ConnectionRowViewModel? _selectedRow;

    public void Initialize()
    {
        if (IsAutoRefreshEnabled && !_monitor.IsRunning)
        {
            _monitor.Start();
        }
    }

    public void Dispose()
    {
        _monitor.Updated -= OnMonitorUpdated;
        _monitor.Failed -= OnMonitorFailed;
    }

    partial void OnIsAutoRefreshEnabledChanged(bool value)
    {
        if (value && !_monitor.IsRunning)
        {
            _monitor.Start();
        }
        else if (!value && _monitor.IsRunning)
        {
            _ = _monitor.StopAsync();
        }
    }

    [RelayCommand]
    private void Refresh() => _ = _monitor.RefreshOnceAsync();

    [RelayCommand]
    private void CopyIp(ConnectionRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row?.CanDiagnose != true)
        {
            return;
        }

        Clipboard.SetText(row.RemoteIp);
        StatusMessage = $"Скопировано: {row.RemoteIp}";
    }

    [RelayCommand]
    private void CopyIpPort(ConnectionRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row?.CanDiagnose != true)
        {
            return;
        }

        Clipboard.SetText(row.RemoteIpPort);
        StatusMessage = $"Скопировано: {row.RemoteIpPort}";
    }

    [RelayCommand]
    private async Task Ping(ConnectionRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row?.CanDiagnose == true)
        {
            await _launcher.PingOnceAsync(row.Info.DiagnosticAddress);
        }
    }

    [RelayCommand]
    private async Task ContinuousPing(ConnectionRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row?.CanDiagnose == true)
        {
            await _launcher.ContinuousPingAsync(row.Info.DiagnosticAddress);
        }
    }

    [RelayCommand]
    private async Task TracerouteIcmp(ConnectionRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row?.CanDiagnose == true)
        {
            await _launcher.TracerouteOnceAsync(row.Info.DiagnosticAddress);
        }
    }

    [RelayCommand]
    private async Task MonitorRoute(ConnectionRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row?.CanDiagnose == true)
        {
            await _launcher.MonitorRouteAsync(row.Info.DiagnosticAddress);
        }
    }

    [RelayCommand]
    private async Task ReverseDns(ConnectionRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row?.CanDiagnose == true)
        {
            await _launcher.ReverseDnsAsync(row.Info.DiagnosticAddress);
        }
    }

    private bool RowMatchesFilter(object item)
    {
        if (item is not ConnectionRowViewModel row || string.IsNullOrWhiteSpace(FilterText))
        {
            return true;
        }

        var needle = FilterText.Trim();
        return row.Info.ProcessName.Contains(needle, StringComparison.OrdinalIgnoreCase)
               || row.Info.ProcessPath.Contains(needle, StringComparison.OrdinalIgnoreCase)
               || row.LocalEndPoint.Contains(needle, StringComparison.OrdinalIgnoreCase)
               || row.RemoteEndPoint.Contains(needle, StringComparison.OrdinalIgnoreCase)
               || row.ProcessId.ToString().Contains(needle, StringComparison.Ordinal)
               || row.State.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private void OnMonitorUpdated(IReadOnlyList<ConnectionInfo> snapshot)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.Invoke(() =>
        {
            var existing = Rows.ToDictionary(row => row.Info.Key, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var info in snapshot)
            {
                seen.Add(info.Key);
                if (existing.TryGetValue(info.Key, out var row))
                {
                    row.Update(info);
                }
                else
                {
                    Rows.Add(new ConnectionRowViewModel(info));
                }
            }

            for (var i = Rows.Count - 1; i >= 0; i--)
            {
                if (!seen.Contains(Rows[i].Info.Key))
                {
                    Rows.RemoveAt(i);
                }
            }

            _view.Refresh();
        });
    }

    private void OnMonitorFailed(Exception exception)
    {
        StatusMessage = $"Ошибка опроса соединений: {exception.Message}";
    }
}
