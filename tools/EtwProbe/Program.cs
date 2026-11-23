using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text.Json;
using DotaChecker.Network.Discovery;
using DotaChecker.Network.Discovery.Models;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

var duration = TimeSpan.FromSeconds(300);
var pidFilter = 0;
var rawPidFilter = 0;
var reportPath = Path.Combine("artifacts", "etw-validation.json");

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--duration" when i + 1 < args.Length:
            duration = TimeSpan.FromSeconds(Math.Clamp(int.Parse(args[++i]), 5, 3600));
            break;
        case "--pid" when i + 1 < args.Length:
            pidFilter = int.Parse(args[++i]);
            break;
        case "--raw-pid" when i + 1 < args.Length:
            rawPidFilter = int.Parse(args[++i]);
            break;
        case "--out" when i + 1 < args.Length:
            reportPath = args[++i];
            break;
        case "--help":
            PrintUsage();
            return 0;
    }
}

var identity = WindowsIdentity.GetCurrent();
var principal = new WindowsPrincipal(identity);
var elevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
Console.WriteLine($"User={identity.Name} Elevated={elevated}");
Console.WriteLine($"Duration={duration.TotalSeconds}s PhaseB_PidFilter={(pidFilter == 0 ? "self (loopback generator)" : pidFilter.ToString())} PhaseA_PidFilter={(rawPidFilter == 0 ? "none (global ETW stream)" : rawPidFilter.ToString())}");

if (!elevated)
{
    Console.WriteLine();
    Console.WriteLine("ETW kernel provider requires Administrator. Run from an elevated PowerShell:");
    Console.WriteLine(@"  Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File .\tools\EtwProbe\run-elevated.ps1 -Duration 300'");
    Console.WriteLine("Or right-click PowerShell -> Run as administrator, then:");
    Console.WriteLine("  cd 'C:\\Users\\moisey29\\Downloads\\Dota Checker'");
    Console.WriteLine("  dotnet run --project tools/EtwProbe -c Release -- --duration 300");
    return 3;
}

var raw = await InspectRawProviderAsync(TimeSpan.FromSeconds(Math.Min(10, duration.TotalSeconds)), rawPidFilter);
Console.WriteLine();
Console.WriteLine("=== Phase A: raw ETW payload ===");
Console.WriteLine($"Session=started Provider=Microsoft-Windows-Kernel-Network Keyword=NetworkTCPIP");
foreach (var pair in raw.EventTypes.OrderByDescending(pair => pair.Value))
{
    Console.WriteLine($"  {pair.Key}: {pair.Value}");
}

Console.WriteLine($"Window={raw.WindowSeconds:F1}s Send={raw.Send} Recv={raw.Receive} Total={raw.Total} " +
                  $"Rate={(raw.WindowSeconds <= 0 ? 0 : raw.Total / raw.WindowSeconds):F1}/s");
foreach (var sample in raw.Samples)
{
    Console.WriteLine($"  SAMPLE {sample}");
}

Console.WriteLine();
Console.WriteLine("=== Phase B: UdpActivityProbe ===");
await using var probe = new UdpActivityProbe();
var tracked = pidFilter > 0 ? new[] { pidFilter } : new[] { Environment.ProcessId };
var status = probe.Start(tracked);
Console.WriteLine($"Status={status} Reason={probe.StatusReason}");
if (status != DotaChecker.Network.Discovery.Models.EtAvailability.Available)
{
    WriteReport(reportPath, elevated, rawPidFilter, raw, probe, duration, 0, 0, 0);
    return 4;
}

var generator = pidFilter > 0 ? Task.CompletedTask : StartUdpNoiseAsync(duration);
var process = Process.GetCurrentProcess();
var cpuBefore = process.TotalProcessorTime;
var workingSetBefore = process.WorkingSet64;

await Task.Delay(duration);
await probe.StopAsync();
await generator;

var cpuAfter = process.TotalProcessorTime;
var workingSetAfter = process.WorkingSet64;
var cpuPercent = (cpuAfter - cpuBefore).TotalMilliseconds / (duration.TotalMilliseconds * Environment.ProcessorCount) * 100;

Console.WriteLine($"PayloadValidated={probe.PayloadValidated} Endpoints={probe.Snapshot().Count}");
Console.WriteLine($"Events={probe.TotalEvents} Send={probe.SendEvents} Recv={probe.ReceiveEvents} Malformed={probe.MalformedEvents}");
Console.WriteLine($"EventsPerSec={probe.EventsPerSecond:F1} CPU={cpuPercent:F2}% WS={workingSetAfter / 1048576.0:F1} MB (delta {(workingSetAfter - workingSetBefore) / 1024.0:F0} KB)");
Console.WriteLine("--- Phase B diagnostic ---");
var snapshotForTotals = probe.Snapshot();
var totalsSize = snapshotForTotals.Sum(item => item.BytesObserved);
var totalsData = snapshotForTotals.Sum(item => item.DataBytesObserved);
Console.WriteLine($"PIDFilter={string.Join(",", tracked)} Send={probe.SendEvents} Receive={probe.ReceiveEvents} " +
                  $"UniqueRemoteEndpoints={probe.Snapshot().Count} EventsPerSec={probe.EventsPerSecond:F1} " +
                  $"PayloadValidated={probe.PayloadValidated} Malformed={probe.MalformedEvents} Dropped={probe.DroppedEvents}");
Console.WriteLine($"BytesObserved(size field)={totalsSize} DataBytesObserved(dsize field)={totalsData} " +
                  "[semantics of `size`/`dsize` not confirmed by this tool]");
Console.WriteLine($"Global(no PID filter): Send={probe.AllSendEvents} Receive={probe.AllReceiveEvents} " +
                  $"TotalByType={probe.AllEventTypeCounts.Values.Sum()}");
foreach (var pair in probe.EventTypeCounts.OrderByDescending(pair => pair.Value))
{
    Console.WriteLine($"  {pair.Key}: {pair.Value}");
}

foreach (var sample in probe.Samples)
{
    Console.WriteLine($"  SAMPLE {sample}");
}

WriteReport(reportPath, elevated, rawPidFilter, raw, probe, duration, cpuPercent, workingSetAfter, workingSetBefore);
Console.WriteLine($"Report={Path.GetFullPath(reportPath)}");
return 0;

static void PrintUsage()
{
    Console.WriteLine("Usage: EtwProbe [--duration SEC] [--pid DOTA2_PID] [--raw-pid PID] [--out report.json]");
    Console.WriteLine("--pid filters Phase B (UdpActivityProbe). --raw-pid filters Phase A; without it Phase A is global.");
    Console.WriteLine("Run elevated. Without --pid the tool generates loopback UDP traffic for self-validation.");
}

static string MaskEndPoint(string endpoint)
{
    var index = endpoint.LastIndexOf(':');
    if (index <= 0)
    {
        return "masked";
    }

    var addressText = endpoint[..index];
    var port = endpoint[(index + 1)..];
    return IPAddress.TryParse(addressText, out var address)
        ? $"{Mask(address)}:{port}"
        : $"masked:{port}";
}

static string Mask(object? value)
{
    if (value is null)
    {
        return "null";
    }

    var text = value.ToString() ?? string.Empty;
    if (value is IPAddress address)
    {
        var parts = text.Split('.');
        return parts.Length == 4 ? $"*.{parts[1]}.{parts[2]}.{parts[3]}" : "*:*";
    }

    return text.Length > 32 ? text[..32] + "..." : text;
}

static async Task<RawResult> InspectRawProviderAsync(TimeSpan duration, int rawPidFilter)
{
    var result = new RawResult { WindowSeconds = duration.TotalSeconds };
    using var session = new TraceEventSession("DotaChecker-EtwProbe-Raw");
    session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

    void OnEvent(TraceEvent data)
    {
        if (rawPidFilter > 0)
        {
            var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var name in data.PayloadNames)
            {
                payload[name] = data.PayloadByName(name);
            }

            var eventPid = UdpActivityEventParser.TryParse(data.EventName, (int)data.ID, data.Version,
                payload, data.TimeStamp, out var parsed) && parsed.ProcessId > 0
                    ? parsed.ProcessId
                    : data.ProcessID;
            if (eventPid != rawPidFilter)
            {
                return;
            }
        }

        var type = $"{data.EventName}|id={(int)data.ID}|v{data.Version}";
        result.EventTypes.AddOrUpdate(type, 1, (_, count) => count + 1);
        result.Total++;
        var direction = UdpActivityEventParser.ResolveDirection(data.EventName, (int)data.ID);
        if (direction == UdpDirection.Send)
        {
            result.Send++;
        }
        else if (direction == UdpDirection.Receive)
        {
            result.Receive++;
        }

        if (result.Samples.Count < 20)
        {
            var fields = data.PayloadNames.Select(name => $"{name}:{data.PayloadByName(name)?.GetType().Name ?? "null"}={Mask(data.PayloadByName(name))}");
            result.Samples.Enqueue($"{type} headerPID={data.ProcessID} fields=[{string.Join(", ", fields)}]");
        }
    }

    session.Source.Kernel.UdpIpSend += OnEvent;
    session.Source.Kernel.UdpIpRecv += OnEvent;

    var processing = Task.Run(() =>
    {
        try
        {
            session.Source.Process();
        }
        catch (Exception exception)
        {
            result.Error = exception.Message;
        }
    });

    var noise = StartUdpNoiseAsync(duration);
    await Task.Delay(duration);
    session.Stop();
    await noise;
    await Task.WhenAny(processing, Task.Delay(3000));
    session.Dispose();
    return result;
}

static async Task StartUdpNoiseAsync(TimeSpan duration)
{
    using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 39871));
    using var sender = new UdpClient();
    var target = new IPEndPoint(IPAddress.Loopback, 39871);
    var payload = new byte[96];
    var stop = DateTimeOffset.UtcNow + duration;

    while (DateTimeOffset.UtcNow < stop)
    {
        await sender.SendAsync(payload, target);
        var receive = listener.ReceiveAsync();
        var completed = await Task.WhenAny(receive, Task.Delay(200));
        if (completed == receive)
        {
            _ = await receive;
        }

        await Task.Delay(50);
    }
}

static void WriteReport(string path, bool elevated, int rawPidFilter, RawResult raw, UdpActivityProbe probe,
    TimeSpan duration, double cpuPercent, long workingSet, long workingSetBefore)
{
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    var report = new
    {
        generatedAt = DateTimeOffset.UtcNow,
        elevated,
        durationSeconds = duration.TotalSeconds,
        raw = new
        {
            pidFilter = rawPidFilter,
            windowSeconds = raw.WindowSeconds,
            eventsPerSecond = raw.WindowSeconds <= 0 ? 0 : raw.Total / raw.WindowSeconds,
            provider = "Microsoft-Windows-Kernel-Network",
            keyword = "NetworkTCPIP",
            eventTypes = raw.EventTypes,
            send = raw.Send,
            receive = raw.Receive,
            total = raw.Total,
            error = raw.Error,
            samples = raw.Samples.ToArray(),
        },
        probe = new
        {
            status = probe.Status.ToString(),
            statusReason = probe.StatusReason,
            payloadValidated = probe.PayloadValidated,
            events = probe.TotalEvents,
            send = probe.SendEvents,
            receive = probe.ReceiveEvents,
            malformed = probe.MalformedEvents,
            dropped = probe.DroppedEvents,
            bytesObservedSizeField = probe.Snapshot().Sum(item => item.BytesObserved),
            dataBytesObservedDsizeField = probe.Snapshot().Sum(item => item.DataBytesObserved),
            sizeSemanticsConfirmed = false,
            allSend = probe.AllSendEvents,
            allReceive = probe.AllReceiveEvents,
            eventsPerSecond = probe.EventsPerSecond,
            elapsedSeconds = probe.Elapsed.TotalSeconds,
            eventTypeCounts = probe.EventTypeCounts,
            endpoints = probe.Snapshot().Select(item => new
            {
                pid = item.ProcessId,
                remoteAddressMasked = Mask(item.RemoteAddress),
                remotePort = item.RemotePort,
                localEndPointMasked = MaskEndPoint(item.LocalEndPoint),
                eventCount = item.EventCount,
                send = item.SendCount,
                receive = item.ReceiveCount,
                bytesObservedSizeField = item.BytesObserved,
                dataBytesObservedDsizeField = item.DataBytesObserved,
                firstSeen = item.FirstSeen,
                lastSeen = item.LastSeen,
            }).ToArray(),
            endpointCount = probe.Snapshot().Count,
            samples = probe.Samples,
        },
        load = new { cpuPercent, workingSetBytes = workingSet, workingSetDeltaBytes = workingSet - workingSetBefore },
    };

    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json);
}

internal sealed class RawResult
{
    public System.Collections.Concurrent.ConcurrentDictionary<string, long> EventTypes { get; } = new(StringComparer.Ordinal);
    public System.Collections.Concurrent.ConcurrentQueue<string> Samples { get; } = new();
    public long Send;
    public long Receive;
    public long Total;
    public double WindowSeconds;
    public string? Error;
}
