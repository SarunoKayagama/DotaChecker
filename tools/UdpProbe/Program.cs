using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using DotaChecker.Network.Udp;

var targetText = args.Length > 0 ? args[0] : "127.0.0.1";
var destinationPort = args.Length > 1 && int.TryParse(args[1], out var port) ? port : 33434;
var ttl = args.Length > 2 && int.TryParse(args[2], out var parsedTtl) ? parsedTtl : 1;
var sourcePort = args.Length > 3 && int.TryParse(args[3], out var parsedSource) ? parsedSource : 51000;
var timeoutMs = args.Length > 4 && int.TryParse(args[4], out var parsedTimeout) ? parsedTimeout : 2000;

if (!IPAddress.TryParse(targetText, out var target))
{
    Console.WriteLine("Target must be an IPv4 literal.");
    return 2;
}

Console.WriteLine($"UDP probe -> {target}:{destinationPort}, ttl={ttl}, srcPort={sourcePort}, timeout={timeoutMs}ms");
Console.WriteLine("NOTE: UDP TTL send works without elevation; receiving ICMP errors requires raw socket (Administrator).");

Socket? receiver = null;
try
{
    receiver = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Icmp);
    receiver.Bind(new IPEndPoint(IPAddress.Any, 0));
    receiver.ReceiveTimeout = timeoutMs;
    Console.WriteLine("Raw ICMP socket created (creation allowed; receive ability not proven by this check).");
}
catch (SocketException exception) when (exception.SocketErrorCode == SocketError.AccessDenied)
{
    Console.WriteLine("Raw ICMP socket: AccessDenied -> Administrator required. Send-only check will continue.");
}
catch (SocketException exception)
{
    Console.WriteLine($"Raw ICMP socket error: {exception.SocketErrorCode}");
}

using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
sender.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.IpTimeToLive, ttl);
sender.Bind(new IPEndPoint(IPAddress.Any, sourcePort));

var payload = new byte[64];
var stopwatch = Stopwatch.StartNew();
try
{
    sender.SendTo(payload, new IPEndPoint(target, destinationPort));
    Console.WriteLine($"UDP datagram sent, RTT start {stopwatch.ElapsedMilliseconds}ms");
}
catch (SocketException exception)
{
    Console.WriteLine($"UDP send failed: {exception.SocketErrorCode}");
    receiver?.Dispose();
    return 3;
}

if (receiver is null)
{
    receiver?.Dispose();
    return 4;
}

var buffer = new byte[2048];
var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
while (DateTimeOffset.UtcNow < deadline)
{
    try
    {
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        var received = receiver.ReceiveFrom(buffer, ref remote);
        var ipHeaderLengthRaw = (buffer[0] & 0x0F) * 4;
        Console.WriteLine($"RAW ICMP packet: len={received}, type={buffer[ipHeaderLengthRaw]}, code={buffer[ipHeaderLengthRaw + 1]}");
        var reply = UdpIcmpErrorParser.Parse(buffer[..received], (IPAddress)((IPEndPoint)remote).Address,
            stopwatch.Elapsed.TotalMilliseconds);
        if (reply is null)
        {
            continue;
        }

        Console.WriteLine($"ICMP {reply.Kind} from {reply.Responder}, quoted UDP {reply.QuotedSourcePort}->{reply.QuotedDestinationPort}, rtt={reply.RttMs:F1}ms");
        if (reply.QuotedSourcePort == sourcePort && reply.QuotedDestinationPort == destinationPort)
        {
            Console.WriteLine("MATCH: this error belongs to our probe.");
            receiver.Dispose();
            return 0;
        }

        Console.WriteLine("MISMATCH: error belongs to another socket.");
    }
    catch (SocketException exception) when (exception.SocketErrorCode == SocketError.TimedOut)
    {
        break;
    }
}

Console.WriteLine("Timed out waiting for matching ICMP error.");
receiver.Dispose();
return 5;
