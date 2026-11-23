using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using DotaChecker.Network.Windows;

namespace DotaChecker.Network.Connections;

public sealed record ProcessInfo(int ProcessId, string Name, string Path, bool LimitedAccess);

/// <summary>PID → имя и путь процесса, с кэшем и обработкой Access Denied.</summary>
public sealed class ProcessResolver : IDisposable
{
    private readonly ConcurrentDictionary<int, CacheEntry> _cache = new();
    private readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(30);

    public ProcessInfo Resolve(int processId)
    {
        if (_cache.TryGetValue(processId, out var cached) &&
            DateTimeOffset.UtcNow - cached.CachedAt < _cacheTtl)
        {
            return cached.Info;
        }

        var info = ResolveCore(processId);
        _cache[processId] = new CacheEntry(info, DateTimeOffset.UtcNow);
        return info;
    }

    public void Dispose()
    {
        foreach (var pid in _cache.Keys.ToArray())
        {
            _cache.TryRemove(pid, out _);
        }
    }

    private static ProcessInfo ResolveCore(int processId)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle != IntPtr.Zero)
        {
            try
            {
                var builder = new StringBuilder(1024);
                uint size = (uint)builder.Capacity;
                if (NativeMethods.QueryFullProcessImageNameW(handle, 0, builder, ref size))
                {
                    var path = builder.ToString();
                    return new ProcessInfo(processId, Path.GetFileNameWithoutExtension(path), path, false);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return new ProcessInfo(processId, process.ProcessName, "<access denied>", true);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return new ProcessInfo(processId, $"<pid {processId}>", "<unknown>", true);
        }
    }

    private sealed record CacheEntry(ProcessInfo Info, DateTimeOffset CachedAt);
}
