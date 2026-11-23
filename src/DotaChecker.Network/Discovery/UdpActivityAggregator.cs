using System.Collections.Concurrent;
using DotaChecker.Network.Discovery.Models;

namespace DotaChecker.Network.Discovery;

/// <summary>
/// Ограниченная агрегация ETW-событий по endpoint. Не хранит raw payload и не
/// передаёт каждое событие в UI — обновление снимков выполняется по запросу.
/// </summary>
public sealed class UdpActivityAggregator
{
    private const int DefaultCapacity = 500;

    private readonly ConcurrentDictionary<string, MutableAggregate> _entries = new();
    private readonly int _capacity;
    private readonly Func<int, string> _processNameResolver;

    public UdpActivityAggregator(
        int capacity = DefaultCapacity,
        Func<int, string>? processNameResolver = null)
    {
        _capacity = Math.Max(1, capacity);
        _processNameResolver = processNameResolver ?? (pid => $"<pid {pid}>");
    }

    public long TotalEvents { get; private set; }

    public long MalformedEvents { get; private set; }

    public void Record(UdpActivityEvent item)
    {
        var key = UdpActivityEventParser.BuildKey(item);
        _entries.AddOrUpdate(
            key,
            _ => new MutableAggregate(item, _processNameResolver(item.ProcessId)),
            (_, existing) =>
            {
                existing.Update(item);
                return existing;
            });

        TotalEvents++;

        if (_entries.Count > _capacity)
        {
            EvictOldest();
        }
    }

    public void RecordMalformed() => MalformedEvents++;

    public IReadOnlyList<UdpActivityAggregate> Snapshot() =>
        _entries.Values
            .Select(entry => entry.ToRecord())
            .OrderByDescending(entry => entry.LastSeen)
            .ToArray();

    public void Clear()
    {
        _entries.Clear();
        TotalEvents = 0;
        MalformedEvents = 0;
    }

    private void EvictOldest()
    {
        var oldest = _entries
            .OrderBy(pair => pair.Value.LastSeen)
            .Take(_entries.Count - _capacity)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var key in oldest)
        {
            _entries.TryRemove(key, out _);
        }
    }

    private sealed class MutableAggregate
    {
        private readonly object _gate = new();

        public MutableAggregate(UdpActivityEvent item, string processName)
        {
            Key = UdpActivityEventParser.BuildKey(item);
            ProcessId = item.ProcessId;
            ProcessName = processName;
            RemoteAddress = item.RemoteAddress;
            RemotePort = item.RemotePort;
            LocalEndPoint = $"{item.LocalAddress}:{item.LocalPort}";
            FirstSeen = item.Timestamp;
            LastSeen = item.Timestamp;
            EventCount = 1;
            SendCount = item.Direction == Models.UdpDirection.Send ? 1 : 0;
            ReceiveCount = item.Direction == Models.UdpDirection.Receive ? 1 : 0;
            BytesObserved = item.SizeBytes;
            DataBytesObserved = item.DataSizeBytes;
            ConnectionId = item.ConnectionId;
        }

        public string Key { get; }

        public int ProcessId { get; }

        public string ProcessName { get; }

        public System.Net.IPAddress RemoteAddress { get; }

        public int RemotePort { get; }

        public string LocalEndPoint { get; }

        public DateTimeOffset FirstSeen { get; private set; }

        public DateTimeOffset LastSeen { get; private set; }

        public long EventCount { get; private set; }

        public long SendCount { get; private set; }

        public long ReceiveCount { get; private set; }

        public long BytesObserved { get; private set; }

        public long DataBytesObserved { get; private set; }

        public long ConnectionId { get; private set; }

        public void Update(UdpActivityEvent item)
        {
            lock (_gate)
            {
                if (item.Timestamp < FirstSeen)
                {
                    FirstSeen = item.Timestamp;
                }

                if (item.Timestamp > LastSeen)
                {
                    LastSeen = item.Timestamp;
                }

                EventCount++;
                if (item.Direction == Models.UdpDirection.Send)
                {
                    SendCount++;
                }
                else if (item.Direction == Models.UdpDirection.Receive)
                {
                    ReceiveCount++;
                }

                BytesObserved += item.SizeBytes;
                DataBytesObserved += item.DataSizeBytes;
                if (ConnectionId == 0 && item.ConnectionId != 0)
                {
                    ConnectionId = item.ConnectionId;
                }
            }
        }

        public UdpActivityAggregate ToRecord()
        {
            lock (_gate)
            {
                return new UdpActivityAggregate(
                    Key,
                    ProcessId,
                    ProcessName,
                    RemoteAddress,
                    RemotePort,
                    LocalEndPoint,
                    FirstSeen,
                    LastSeen,
                    EventCount,
                    SendCount,
                    ReceiveCount,
                    BytesObserved,
                    DataBytesObserved,
                    ConnectionId);
            }
        }
    }
}
