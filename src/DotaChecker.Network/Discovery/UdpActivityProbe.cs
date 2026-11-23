using System.Collections.Concurrent;
using DotaChecker.Network.Discovery.Models;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace DotaChecker.Network.Discovery;

/// <summary>
/// Прототип ETW-наблюдателя UDP-активности. Отдельный модуль: не меняет
/// ConnectionMonitor, ContinuousProbeEngine и StatisticsEngine.
///
/// ВАЖНО: запуск ETW kernel-провайдера требует прав администратора. Если сессия
/// недоступна, модуль сообщает статус и не имитирует обнаружение endpoint.
/// </summary>
public sealed class UdpActivityProbe : IAsyncDisposable
{
    private const int MaxSamples = 10;

    private readonly UdpActivityAggregator _aggregator = new();
    private readonly ConcurrentDictionary<int, byte> _trackedPids = new();
    private readonly ConcurrentDictionary<string, long> _eventTypeCounts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _allEventTypeCounts = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _samples = new();
    private readonly ConcurrentQueue<string> _allSamples = new();
    private DateTimeOffset _startedAt;
    private long _sendEvents;
    private long _receiveEvents;
    private long _allSendEvents;
    private long _allReceiveEvents;
    private long _droppedEvents;

    private TraceEventSession? _session;
    private Task? _processing;
    private CancellationTokenSource? _cancellation;

    public EtAvailability Status { get; private set; } = EtAvailability.Unknown;

    public string StatusReason { get; private set; } = "Модуль не запускался.";

    /// <summary>true хотя бы после одного реального события, успешно разобранного парсером.</summary>
    public bool PayloadValidated { get; private set; }

    public IReadOnlyList<string> Samples => _samples.ToArray();

    public EtAvailability Start(IEnumerable<int>? processIds = null)
    {
        ClearTrackedProcesses();
        foreach (var pid in processIds ?? Array.Empty<int>())
        {
            if (pid > 0)
            {
                _trackedPids[pid] = 0;
            }
        }

        try
        {
            _session = new TraceEventSession("DotaChecker-UdpActivityProbe");
            _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
            _session.Source.Kernel.UdpIpSend += OnEvent;
            _session.Source.Kernel.UdpIpRecv += OnEvent;

            _startedAt = DateTimeOffset.UtcNow;
            _cancellation = new CancellationTokenSource();
            _processing = Task.Run(() =>
            {
                try
                {
                    _session.Source.Process();
                }
                catch (Exception exception)
                {
                    SetStatus(EtAvailability.Error, $"ETW processing: {exception.Message}");
                }
            }, _cancellation.Token);

            Status = EtAvailability.Available;
            StatusReason = "ETW-сессия запущена; поля ещё требуют проверки на реальных событиях.";
            return Status;
        }
        catch (UnauthorizedAccessException exception)
        {
            SetStatus(EtAvailability.PermissionDenied, exception.Message);
            return Status;
        }
        catch (Exception exception)
        {
            var status = exception.Message.Contains("provider", StringComparison.OrdinalIgnoreCase)
                ? EtAvailability.ProviderUnavailable
                : EtAvailability.Error;
            SetStatus(status, exception.Message);
            return Status;
        }
    }

    public void SetTrackedProcessIds(IEnumerable<int> processIds)
    {
        ClearTrackedProcesses();
        foreach (var pid in processIds)
        {
            if (pid > 0)
            {
                _trackedPids[pid] = 0;
            }
        }
    }

    public IReadOnlyList<UdpActivityAggregate> Snapshot() => _aggregator.Snapshot();

    public long TotalEvents => _aggregator.TotalEvents;

    public long MalformedEvents => _aggregator.MalformedEvents;

    public long SendEvents => Interlocked.Read(ref _sendEvents);

    public long ReceiveEvents => Interlocked.Read(ref _receiveEvents);

    public IReadOnlyDictionary<string, long> EventTypeCounts =>
        _eventTypeCounts.ToDictionary(pair => pair.Key, pair => pair.Value);

    public TimeSpan Elapsed => _startedAt == default ? TimeSpan.Zero : DateTimeOffset.UtcNow - _startedAt;

    public double EventsPerSecond => Elapsed.TotalSeconds <= 0 ? 0 : TotalEvents / Elapsed.TotalSeconds;

    /// <summary>Счётчики всех событий provider (без PID-фильтра), для контекста.</summary>
    public long AllSendEvents => Interlocked.Read(ref _allSendEvents);

    public long AllReceiveEvents => Interlocked.Read(ref _allReceiveEvents);

    public IReadOnlyDictionary<string, long> AllEventTypeCounts =>
        _allEventTypeCounts.ToDictionary(pair => pair.Key, pair => pair.Value);

    public long DroppedEvents => Interlocked.Read(ref _droppedEvents);

    public IReadOnlyList<string> AllSamples => _allSamples.ToArray();

    public async Task StopAsync()
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            Interlocked.Exchange(ref _droppedEvents, Convert.ToInt64(_session.Source.EventsLost));
            _session.Stop();
        }
        catch (Exception exception)
        {
            SetStatus(EtAvailability.Error, $"ETW stop: {exception.Message}");
        }

        if (_processing is not null)
        {
            try
            {
                await _processing.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                SetStatus(EtAvailability.Error, $"ETW wait: {exception.Message}");
            }
        }

        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        _session?.Dispose();
        _session = null;
        _processing = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private void OnEvent(TraceEvent data)
    {
        // ETW переиспользует объекты событий: всё извлекаем сразу же.
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var name in data.PayloadNames)
        {
            payload[name] = data.PayloadByName(name);
        }

        payload["HeaderProcessId"] = data.ProcessID;

        var eventType = $"{data.EventName}|id={(int)data.ID}|v{data.Version}";
        var direction = UdpActivityEventParser.ResolveDirection(data.EventName, (int)data.ID);

        // Глобальные счётчики нужны только для контекста провайдера.
        _allEventTypeCounts.AddOrUpdate(eventType, 1, (_, count) => count + 1);
        if (direction == UdpDirection.Send)
        {
            Interlocked.Increment(ref _allSendEvents);
        }
        else if (direction == UdpDirection.Receive)
        {
            Interlocked.Increment(ref _allReceiveEvents);
        }

        if (_allSamples.Count < MaxSamples)
        {
            var allFields = string.Join(", ", data.PayloadNames.Select(name => $"{name}:{payload[name]?.GetType().Name ?? "null"}"));
            _allSamples.Enqueue($"event={data.EventName} id={(int)data.ID} version={data.Version} headerPID={data.ProcessID} fields=[{allFields}]");
        }

        if (_trackedPids.IsEmpty)
        {
            return;
        }

        var parsed = false;
        var pid = 0;
        if (UdpActivityEventParser.TryParse(
                data.EventName,
                (int)data.ID,
                data.Version,
                payload,
                data.TimeStamp,
                out var activity))
        {
            parsed = true;
            pid = activity.ProcessId;
        }
        else if (payload.TryGetValue("HeaderProcessId", out var headerPid) && headerPid is int header)
        {
            // Неизвестный формат payload: PID берём из заголовка ETW только для фильтрации.
            pid = header;
        }

        if (!_trackedPids.ContainsKey(pid))
        {
            return;
        }

        if (!parsed)
        {
            _aggregator.RecordMalformed();
            return;
        }

        // Отсюда всё относится только к отслеживаемым PID.
        _eventTypeCounts.AddOrUpdate(eventType, 1, (_, count) => count + 1);
        if (direction == UdpDirection.Send)
        {
            Interlocked.Increment(ref _sendEvents);
        }
        else if (direction == UdpDirection.Receive)
        {
            Interlocked.Increment(ref _receiveEvents);
        }

        if (_samples.Count < MaxSamples)
        {
            var fields = string.Join(", ", data.PayloadNames.Select(name => $"{name}:{payload[name]?.GetType().Name ?? "null"}"));
            _samples.Enqueue($"event={data.EventName} id={(int)data.ID} version={data.Version} headerPID={data.ProcessID} fields=[{fields}]");
        }

        PayloadValidated = true;
        _aggregator.Record(activity);
    }

    private void ClearTrackedProcesses() => _trackedPids.Clear();

    private void SetStatus(EtAvailability status, string reason)
    {
        Status = status;
        StatusReason = reason;
    }
}
