using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotaChecker.Core.Diagnostics;
using DotaChecker.Core.Models;
using DotaChecker.Network.Monitoring;

namespace DotaChecker.App.ViewModels;

public sealed partial class RouteMonitorViewModel : ObservableObject, IDisposable
{
    private readonly ContinuousProbeEngine _engine;
    private readonly RouteAnalyzer _analyzer;
    private readonly Dictionary<int, HopRowViewModel> _rowsByHop = new();
    private DateTimeOffset _lastAnalysisAt = DateTimeOffset.MinValue;

    public RouteMonitorViewModel(ContinuousProbeEngine engine, RouteAnalyzer analyzer)
    {
        _engine = engine;
        _analyzer = analyzer;
        _engine.SnapshotPublished += OnSnapshot;
        _engine.RouteChanged += OnRouteChanged;
        _engine.ErrorOccurred += message => StatusMessage = message;
        _selectedHop = E2e;
        RefreshGraph();
    }

    public ObservableCollection<HopRowViewModel> Hops { get; } = new();

    public HopRowViewModel E2e { get; } = new();

    [ObservableProperty]
    private string _targetInput = "8.8.8.8";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _roundText = "раунд 0";

    [ObservableProperty]
    private HopRowViewModel _selectedHop;

    [ObservableProperty]
    private IReadOnlyList<double> _rttSeries = Array.Empty<double>();

    [ObservableProperty]
    private IReadOnlyList<bool> _lossSeries = Array.Empty<bool>();

    [ObservableProperty]
    private string _routeEventsText = string.Empty;

    [ObservableProperty]
    private string _measuredText = "—";

    [ObservableProperty]
    private string _uncertaintyText = "—";

    [ObservableProperty]
    private string _hypothesisText = "—";

    [ObservableProperty]
    private string _limitationsText = "—";

    [ObservableProperty]
    private bool _isControlledExperiment;

    public bool IsIcmpOnly => true;

    [RelayCommand]
    private async Task StartAsync()
    {
        if (_engine.IsRunning)
        {
            StatusMessage = "Мониторинг уже запущен.";
            return;
        }

        var target = await ResolveTargetAsync(TargetInput);
        if (target is null)
        {
            StatusMessage = "Не удалось разрешить IP-адрес цели.";
            return;
        }

        try
        {
            ClearRows();
            await _engine.StartAsync(target);
            IsRunning = true;
            StatusMessage = $"Мониторинг запущен: {target}. ICMP, права администратора не требуются.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Ошибка запуска: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        await _engine.StopAsync();
        IsRunning = false;
        StatusMessage = "Мониторинг остановлен. Накопленная статистика сохранена.";
    }

    [RelayCommand]
    private void ResetStatistics()
    {
        var answer = MessageBox.Show(
            "Сбросить всю накопленную статистику текущей сессии?",
            "Dota Checker",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _engine.ResetStatistics();
        StatusMessage = "Статистика сброшена вручную.";
    }

    [RelayCommand]
    private void Analyze() => RunAnalysis(IsControlledExperiment);

    [RelayCommand]
    private void AnalyzeControlled() => RunAnalysis(true);

    private void RunAnalysis(bool controlled)
    {
        try
        {
            var result = _analyzer.Analyze(_engine.CreateAnalysisInput(controlled));
            MeasuredText = string.Join(Environment.NewLine, result.Measurements
                .Select(item => $"hop {item.HopIndex}: нет ответа {item.LossPct:F1}% " +
                                "(95% CI " +
                                $"{item.CiLow:P1}…{item.CiHigh:P1}), n={item.Sent}, " +
                                $"φ₁={item.Lag1Autocorrelation:F2}, max run={item.LongestLossRun}"));
            MeasuredText += Environment.NewLine +
                $"e2e: нет ответа {result.E2eMeasurement.LossPct:F1}%, n={result.E2eMeasurement.Sent}";

            UncertaintyText = string.Join(Environment.NewLine,
                $"предварительный z={result.Uncertainty.PreliminaryZ:F2} (предполагает независимость)",
                $"block bootstrap разницы долей: " +
                $"[{result.Uncertainty.BootstrapDifferenceLow:P1} … {result.Uncertainty.BootstrapDifferenceHigh:P1}], " +
                $"длина блока={result.Uncertainty.BlockLength}, итераций={result.Uncertainty.BootstrapIterations}",
                $"автокорреляция φ₁={result.Uncertainty.Lag1Autocorrelation:F2}");
            UncertaintyText += Environment.NewLine + string.Join(Environment.NewLine, result.Uncertainty.Notes);

            HypothesisText = string.Join(Environment.NewLine,
                $"вердикт: {result.Hypothesis.Verdict}",
                $"локализация: {result.SegmentLabel}",
                $"EvidenceScore: {result.Hypothesis.EvidenceScore:F2} (не вероятность)",
                result.Hypothesis.Explanation,
                $"рекомендация: {result.Hypothesis.Recommendation}");

            LimitationsText = string.Join(Environment.NewLine, result.Limitations);
            StatusMessage = $"Анализ выполнен: {result.Hypothesis.Verdict}.";
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = $"Анализ невозможен: {exception.Message}";
        }
    }

    partial void OnSelectedHopChanged(HopRowViewModel value) => RefreshGraph();

    public void Dispose()
    {
        _engine.SnapshotPublished -= OnSnapshot;
        _engine.RouteChanged -= OnRouteChanged;
    }

    private static async Task<IPAddress?> ResolveTargetAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var text = input.Trim();
        if (IPAddress.TryParse(text, out var address))
        {
            return address;
        }

        try
        {
            var lookup = System.Net.Dns.GetHostAddressesAsync(text);
            var completed = await Task.WhenAny(lookup, Task.Delay(TimeSpan.FromSeconds(3)));
            if (completed != lookup)
            {
                return null;
            }

            return (await lookup).FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            return null;
        }
    }

    private void ClearRows()
    {
        Hops.Clear();
        _rowsByHop.Clear();
        E2e.Update(new MonitorHopView(0, Array.Empty<IPAddress>(), "—", true, new Core.Statistics.HopStatistics().Snapshot()));
        RefreshGraph();
    }

    private void OnSnapshot(MonitorSnapshot snapshot)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.Invoke(() =>
        {
            var seen = new HashSet<int>();
            foreach (var hop in snapshot.Hops)
            {
                seen.Add(hop.HopIndex);
                if (!_rowsByHop.TryGetValue(hop.HopIndex, out var row))
                {
                    row = new HopRowViewModel();
                    _rowsByHop[hop.HopIndex] = row;
                    Hops.Add(row);
                }

                row.Update(hop);
            }

            for (var i = Hops.Count - 1; i >= 0; i--)
            {
                if (!seen.Contains(Hops[i].HopIndex))
                {
                    Hops.RemoveAt(i);
                    _rowsByHop.Remove(Hops[i].HopIndex);
                }
            }

            E2e.Update(snapshot.E2e);
            RoundText = $"раунд {snapshot.Round} · {snapshot.Hops.Count} узлов";
            RefreshGraph();

            if (DateTimeOffset.UtcNow - _lastAnalysisAt >= TimeSpan.FromSeconds(5))
            {
                _lastAnalysisAt = DateTimeOffset.UtcNow;
                RunAnalysis(IsControlledExperiment);
            }
        });
    }

    private void OnRouteChanged(IReadOnlyList<RouteChangeEvent> events)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.Invoke(() =>
        {
            var lines = events.Select(item => $"{item.OccurredAt.LocalDateTime:HH:mm:ss} {item.Kind}: {item.Description}");
            RouteEventsText = string.Join(Environment.NewLine, lines);
            StatusMessage = "Обнаружено изменение маршрута. Статистика разделена по версиям.";
        });
    }

    private void RefreshGraph()
    {
        var source = SelectedHop ?? E2e;
        RttSeries = source.RttHistory;
        LossSeries = source.SuccessHistory;
    }
}
