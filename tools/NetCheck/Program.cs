using System.Diagnostics;
using System.Net;
using DotaChecker.Core.Diagnostics;
using DotaChecker.Network.Icmp;

var targetText = args.Length > 0 ? args[0] : "8.8.8.8";
var maxTtl = args.Length > 1 && int.TryParse(args[1], out var parsed) ? parsed : 8;
var target = IPAddress.TryParse(targetText, out var literal) ? literal : (await System.Net.Dns.GetHostAddressesAsync(targetText))[0];
var timeout = TimeSpan.FromMilliseconds(1500);

Console.WriteLine($"NetCheck → {target} (max TTL {maxTtl})");
await using var transport = new IcmpProbeTransport();

Console.WriteLine("\nSequential TTL probes:");
for (var ttl = 1; ttl <= maxTtl; ttl++)
{
    var stopwatch = Stopwatch.StartNew();
    var reply = await transport.SendAsync(new ProbeRequest(ttl, target, ttl, timeout), CancellationToken.None);
    Console.WriteLine($"  ttl={ttl,2} {reply.Outcome,-20} responder={reply.Responder,-15} rtt={reply.RttMs,5} ms status={reply.Status} elapsed={stopwatch.ElapsedMilliseconds} ms");
}

Console.WriteLine("\nParallel round (one probe per TTL):");
var tasks = Enumerable.Range(1, maxTtl)
    .Select(ttl => transport.SendAsync(new ProbeRequest(100 + ttl, target, ttl, timeout), CancellationToken.None))
    .ToArray();
var replies = await Task.WhenAll(tasks);
for (var i = 0; i < replies.Length; i++)
{
    Console.WriteLine($"  ttl={i + 1,2} {replies[i].Outcome,-20} responder={replies[i].Responder,-15} rtt={replies[i].RttMs,5} ms");
}

Console.WriteLine("\nDiscovery (2 queries per hop):");
var discovery = new IcmpRouteDiscovery(transport, queriesPerHop: 2, slotBase: 500);
var route = await discovery.DiscoverAsync(target, maxTtl, timeout, CancellationToken.None);
foreach (var hop in route.Hops)
{
    var addresses = hop.Responders.Count == 0 ? "no reply" : string.Join(",", hop.Responders);
    Console.WriteLine($"  hop {hop.HopIndex,2}: {addresses}{(hop.IsDestination ? " [destination]" : "")}");
}

var e2e = await transport.SendAsync(new ProbeRequest(900, target, 255, timeout), CancellationToken.None);
Console.WriteLine($"\nE2E reference: {e2e.Outcome} responder={e2e.Responder} rtt={e2e.RttMs} ms status={e2e.Status}");
