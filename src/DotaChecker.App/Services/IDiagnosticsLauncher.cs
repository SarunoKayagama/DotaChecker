using System.Net;

namespace DotaChecker.App.Services;

/// <summary>Запуск диагностики из контекстного меню IP-адресов.</summary>
public interface IDiagnosticsLauncher
{
    event Action<string>? StatusMessage;

    event Action? RequestRouteMonitorTab;

    Task PingOnceAsync(IPAddress target);

    Task ContinuousPingAsync(IPAddress target);

    Task TracerouteOnceAsync(IPAddress target);

    Task MonitorRouteAsync(IPAddress target);

    Task ReverseDnsAsync(IPAddress target);
}
