using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace DotaChecker.Network.Dns;

/// <summary>Reverse DNS с кэшем, таймаутом и ограниченным числом параллельных запросов.</summary>
public sealed class DnsResolver : IDisposable
{
    private readonly ConcurrentDictionary<IPAddress, string> _cache = new();
    private readonly SemaphoreSlim _gate = new(4);
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);

    public async Task<string> ResolveAsync(IPAddress address, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(address, out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(address, out cached))
            {
                return cached;
            }

            var lookup = System.Net.Dns.GetHostEntryAsync(address);
            var timeout = Task.Delay(_timeout, cancellationToken);
            var completed = await Task.WhenAny(lookup, timeout).ConfigureAwait(false);

            string result;
            if (completed == lookup)
            {
                try
                {
                    result = (await lookup.ConfigureAwait(false)).HostName;
                }
                catch (Exception exception) when (exception is SocketException or ArgumentException)
                {
                    result = address.ToString();
                }
            }
            else
            {
                result = address.ToString();
            }

            _cache[address] = result;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
