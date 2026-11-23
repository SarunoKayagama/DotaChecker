using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotaChecker.Core.Game;
using DotaChecker.Core.Optimization;
using DotaChecker.Network.Discovery;
using DotaChecker.Network.Monitoring;
using DotaChecker.Network.Routing;

namespace DotaChecker.App.ViewModels;

public sealed partial class RouteOptimizationViewModel : ObservableObject
{
    private readonly RouteComparisonService _comparison;
    private readonly IGameEndpointMonitoringService _monitoring;
    private readonly IGameEndpointDiscoveryService _discovery;
    private readonly RouteInterfaceResolver _interfaces = new();

    public RouteOptimizationViewModel(
        RouteComparisonService comparison,
        IGameEndpointMonitoringService monitoring,
        IGameEndpointDiscoveryService discovery)
    {
        _comparison = comparison;
        _monitoring = monitoring;
        _discovery = discovery;
        _comparison.ComparisonChanged += result => RunOnUi(() => ComparisonText = FormatComparison(result));
    }

    [ObservableProperty]
    private string _endpointText = "Endpoint не выбран";

    [ObservableProperty]
    private string _directText = "Измерение Direct не загружено";

    [ObservableProperty]
    private string _warpText = "Введите данные реального измерения WARP вручную";

    [ObservableProperty]
    private string _comparisonText = "Сравнение не выполнялось";

    [ObservableProperty]
    private string _statusMessage = "WARP не активируется автоматически. Маршрут считается подтверждённым только после проверки интерфейса.";

    [ObservableProperty]
    private string _warpProfileName = "warp";

    [ObservableProperty]
    private int _warpInterfaceIndex;

    [ObservableProperty]
    private string _warpInterfaceName = string.Empty;

    [ObservableProperty]
    private string _warpSourceAddress = string.Empty;

    [ObservableProperty]
    private int _warpSent = 50;

    [ObservableProperty]
    private int _warpReceived = 50;

    [ObservableProperty]
    private double _warpLossPct;

    [ObservableProperty]
    private double _warpJitterMs;

    [ObservableProperty]
    private string _warpRttCsv = string.Empty;

    [ObservableProperty]
    private bool _warpInterfaceVerified;

    [ObservableProperty]
    private string _warpTunnelState = "unknown";

    [RelayCommand]
    private void LoadSelectedEndpoint()
    {
        var record = _discovery.Records().FirstOrDefault();
        if (record is null)
        {
            StatusMessage = "Нет обнаруженных endpoint. Сначала запустите ETW discovery.";
            return;
        }

        EndpointText = $"{record.RemoteAddress}:{record.RemotePort} | {record.Classification?.Category ?? GameEndpointCategory.Unknown} " +
                       $"| {record.Classification?.Evidence ?? EvidenceLevel.Low} (гипотеза)";
        StatusMessage = "Endpoint выбран. Выполните измерение Direct через вкладку Game Servers.";
    }

    [RelayCommand]
    private void LoadDirectFromMonitoring()
    {
        var current = _monitoring.Current;
        if (current is null)
        {
            StatusMessage = "Нет активного ICMP-мониторинга endpoint.";
            return;
        }

        var route = _interfaces.Resolve(IPAddress.Parse(current.RemoteAddress));
        var candidate = new RouteCandidate(
            RouteCandidateKind.Direct,
            "Direct",
            current.EndpointKey,
            current.RemoteAddress,
            route.Index,
            route.Name,
            string.Empty,
            "direct",
            route.Index > 0,
            true);
        var measurement = new RouteMeasurement(
            candidate,
            "ICMP",
            current.UpdatedAt,
            TimeSpan.FromSeconds(Math.Max(1, current.Sent)),
            (int)Math.Min(int.MaxValue, current.Sent),
            (int)Math.Min(int.MaxValue, current.Received),
            current.RttSamples,
            current.ResponseLossPct,
            current.JitterMs,
            0);

        _comparison.SetMeasurement(RouteCandidateKind.Direct, measurement);
        DirectText = FormatMeasurement(measurement) + Environment.NewLine + route.Note;
        StatusMessage = "Direct-измерение загружено из ICMP-мониторинга (не UDP).";
    }

    [RelayCommand]
    private void SetWarpMeasurement()
    {
        var record = _discovery.Records().FirstOrDefault();
        if (record is null)
        {
            StatusMessage = "Endpoint не выбран.";
            return;
        }

        var samples = ParseSamples(WarpRttCsv);
        var candidate = new RouteCandidate(
            RouteCandidateKind.Warp,
            WarpProfileName,
            record.Key,
            record.RemoteAddress.ToString(),
            WarpInterfaceIndex,
            WarpInterfaceName,
            WarpSourceAddress,
            WarpTunnelState,
            WarpInterfaceVerified,
            true);
        var measurement = new RouteMeasurement(
            candidate,
            "ICMP",
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(Math.Max(1, WarpSent)),
            WarpSent,
            Math.Min(WarpReceived, WarpSent),
            samples,
            WarpLossPct,
            WarpJitterMs,
            0);

        _comparison.SetMeasurement(RouteCandidateKind.Warp, measurement);
        WarpText = FormatMeasurement(measurement);
        StatusMessage = "WARP-измерение сохранено из введённых данных. Переключать туннель DotaChecker не будет.";
    }

    [RelayCommand]
    private void Compare()
    {
        var comparison = _comparison.Compare();
        ComparisonText = FormatComparison(comparison);
        StatusMessage = comparison.Explanation;
    }

    [RelayCommand]
    private void Clear()
    {
        _comparison.Clear();
        DirectText = "Измерение Direct не загружено";
        WarpText = "Введите данные реального измерения WARP вручную";
        ComparisonText = "Сравнение не выполнялось";
    }

    private static double[] ParseSamples(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return Array.Empty<double>();
        }

        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => double.TryParse(value.Trim(), out var parsed) ? parsed : double.NaN)
            .Where(value => !double.IsNaN(value))
            .ToArray();
    }

    private static string FormatMeasurement(RouteMeasurement measurement) =>
        string.Join(Environment.NewLine,
            $"Candidate: {measurement.Candidate.Name} ({measurement.Candidate.Kind})",
            $"Interface: {measurement.Candidate.InterfaceName} [{measurement.Candidate.InterfaceIndex}], verified={measurement.Candidate.IsInterfaceVerified}",
            $"Samples: {measurement.Sent}/{measurement.Received}, loss(no reply) {measurement.ResponseLossPct:F1}%",
            $"Jitter {measurement.JitterMs:F1} мс, median {Median(measurement.RttMs):F1} мс",
            "Протокол измерения: " + measurement.Protocol);

    private static double Median(double[] values)
    {
        if (values.Length == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(value => value).ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2.0 : sorted[middle];
    }

    private static string FormatComparison(RouteComparison comparison) =>
        string.Join(Environment.NewLine,
            $"Status: {comparison.Status}",
            $"Endpoint: {comparison.EndpointKey}",
            $"Winner: {comparison.Winner?.Name ?? "—"}",
            $"Valid: {comparison.Validation.IsValid} ({comparison.Validation.Reason})",
            comparison.DirectScore is null ? string.Empty : $"Direct score: {comparison.DirectScore.Overall:F3}",
            comparison.WarpScore is null ? string.Empty : $"WARP score: {comparison.WarpScore.Overall:F3}",
            comparison.Explanation,
            string.Join(Environment.NewLine, comparison.Reasons.Select(reason => "• " + reason)));

    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}
