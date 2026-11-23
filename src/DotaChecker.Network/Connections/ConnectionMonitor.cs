using DotaChecker.Core.Models;
using DotaChecker.Network.Windows;

namespace DotaChecker.Network.Connections;

/// <summary>
/// Периодически опрашивает TCP/UDP-таблицы через IP Helper API и публикует снимки.
/// Не запускает внешние процессы.
/// </summary>
public sealed class ConnectionMonitor : IAsyncDisposable
{
    private readonly ProcessResolver _processResolver = new();
    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    public event Action<IReadOnlyList<ConnectionInfo>>? Updated;

    public event Action<Exception>? Failed;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(2);

    public bool IsRunning => _cancellation is not null;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        _loop = RunAsync(_cancellation.Token);
        _ = RefreshOnceAsync();
    }

    public Task RefreshOnceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = ReadSnapshot();
            Updated?.Invoke(snapshot);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Failed?.Invoke(exception);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cancellation is null)
        {
            return;
        }

        _cancellation.Cancel();
        try
        {
            if (_loop is not null)
            {
                await _loop.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Нормальная остановка.
        }

        _cancellation.Dispose();
        _cancellation = null;
        _loop = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _processResolver.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await RefreshOnceAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private IReadOnlyList<ConnectionInfo> ReadSnapshot()
    {
        var result = new List<ConnectionInfo>();

        foreach (var row in IpHelperTables.ReadTcp())
        {
            var process = _processResolver.Resolve(row.ProcessId);
            result.Add(new ConnectionInfo
            {
                Protocol = TransportProtocol.Tcp,
                LocalAddress = row.LocalAddress,
                LocalPort = row.LocalPort,
                RemoteAddress = row.RemoteAddress,
                RemotePort = row.RemotePort,
                ProcessId = row.ProcessId,
                ProcessName = process.Name,
                ProcessPath = process.Path,
                TcpState = row.State.ToString(),
                CreatedAt = row.CreatedAt,
                HasCreationTime = row.HasCreationTime,
            });
        }

        foreach (var row in IpHelperTables.ReadUdp())
        {
            var process = _processResolver.Resolve(row.ProcessId);
            result.Add(new ConnectionInfo
            {
                Protocol = TransportProtocol.Udp,
                LocalAddress = row.LocalAddress,
                LocalPort = row.LocalPort,
                RemoteAddress = System.Net.IPAddress.Any,
                RemotePort = 0,
                ProcessId = row.ProcessId,
                ProcessName = process.Name,
                ProcessPath = process.Path,
                TcpState = string.Empty,
                CreatedAt = row.CreatedAt,
                HasCreationTime = row.HasCreationTime,
            });
        }

        return result;
    }
}
