using System.Globalization;
using System.Net;
using System.Net.Sockets;
using DotaChecker.Network.Discovery.Models;

namespace DotaChecker.Network.Discovery;

/// <summary>
/// Преобразование «сырого» ETW-события в нормализованную модель.
/// Отделено от ETW-сессии, чтобы тестироваться на заранее подготовленных payload
/// без запуска реального провайдера и без прав администратора.
/// </summary>
public static class UdpActivityEventParser
{
    private static readonly string[] PidNames = { "PID", "pid", "ProcessId", "HeaderProcessId" };
    private static readonly string[] SourceAddressNames = { "saddr", "SourceAddress" };
    private static readonly string[] DestinationAddressNames = { "daddr", "DestinationAddress" };
    private static readonly string[] SourcePortNames = { "sport", "SourcePort" };
    private static readonly string[] DestinationPortNames = { "dport", "DestinationPort" };
    private static readonly string[] SizeNames = { "size", "Size", "PacketSize" };
    private static readonly string[] DataSizeNames = { "dsize", "DSize", "DataSize" };
    private static readonly string[] ConnectionNames = { "connid", "ConnectionId" };

    public static bool TryParse(
        string eventName,
        int eventId,
        int eventVersion,
        IReadOnlyDictionary<string, object?> payload,
        DateTimeOffset timestamp,
        out UdpActivityEvent result)
    {
        result = null!;

        var direction = ResolveDirection(eventName, eventId);
        if (direction == UdpDirection.Unknown)
        {
            return false;
        }

        if (!TryGet(payload, PidNames, out var pidValue) ||
            !TryToInt(pidValue, out var processId) || processId <= 0)
        {
            return false;
        }

        if (!TryGet(payload, SourceAddressNames, out var sourceAddressValue) ||
            !TryGet(payload, DestinationAddressNames, out var destinationAddressValue) ||
            !TryGet(payload, SourcePortNames, out var sourcePortValue) ||
            !TryGet(payload, DestinationPortNames, out var destinationPortValue))
        {
            return false;
        }

        if (!TryToAddress(sourceAddressValue, out var sourceAddress) ||
            !TryToAddress(destinationAddressValue, out var destinationAddress) ||
            !TryToPort(sourcePortValue, out var sourcePort) ||
            !TryToPort(destinationPortValue, out var destinationPort))
        {
            return false;
        }

        var size = 0;
        if (TryGet(payload, SizeNames, out var sizeValue))
        {
            TryToInt(sizeValue, out size);
        }

        var dataSize = 0;
        if (TryGet(payload, DataSizeNames, out var dataSizeValue))
        {
            TryToInt(dataSizeValue, out dataSize);
        }

        var connectionId = 0L;
        if (TryGet(payload, ConnectionNames, out var connectionValue))
        {
            TryToLong(connectionValue, out connectionId);
        }

        var localAddress = direction == UdpDirection.Send ? sourceAddress : destinationAddress;
        var localPort = direction == UdpDirection.Send ? sourcePort : destinationPort;
        var remoteAddress = direction == UdpDirection.Send ? destinationAddress : sourceAddress;
        var remotePort = direction == UdpDirection.Send ? destinationPort : sourcePort;

        if (localPort <= 0 || remotePort <= 0)
        {
            return false;
        }

        result = new UdpActivityEvent(
            processId,
            localAddress,
            localPort,
            remoteAddress,
            remotePort,
            size,
            dataSize,
            connectionId,
            direction,
            timestamp,
            eventName,
            eventId,
            eventVersion);
        return true;
    }

    public static UdpDirection ResolveDirection(string eventName, int eventId)
    {
        if (eventName.Contains("Send", StringComparison.OrdinalIgnoreCase))
        {
            return UdpDirection.Send;
        }

        if (eventName.Contains("Recv", StringComparison.OrdinalIgnoreCase) ||
            eventName.Contains("Receive", StringComparison.OrdinalIgnoreCase))
        {
            return UdpDirection.Receive;
        }

        // UdpIp_TypeGroup1: 10=SendIPV4, 11=RecvIPV4; TypeGroup2: 26/27 для IPv6.
        return eventId switch
        {
            10 or 26 => UdpDirection.Send,
            11 or 27 => UdpDirection.Receive,
            _ => UdpDirection.Unknown,
        };
    }

    private static bool TryGet(
        IReadOnlyDictionary<string, object?> payload,
        IReadOnlyList<string> names,
        out object? value)
    {
        foreach (var name in names)
        {
            if (payload.TryGetValue(name, out value) && value is not null)
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryToInt(object? value, out int result)
    {
        switch (value)
        {
            case int number:
                result = number;
                return true;
            case uint number when number <= int.MaxValue:
                result = (int)number;
                return true;
            case long number when number is >= int.MinValue and <= int.MaxValue:
                result = (int)number;
                return true;
            case string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result):
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryToLong(object? value, out long result)
    {
        if (TryToInt(value, out var number))
        {
            result = number;
            return true;
        }

        switch (value)
        {
            case long longNumber:
                result = longNumber;
                return true;
            case ulong ulongNumber when ulongNumber <= long.MaxValue:
                result = (long)ulongNumber;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryToPort(object? value, out int port)
    {
        if (!TryToInt(value, out port))
        {
            return false;
        }

        return port is > 0 and <= 65535;
    }

    /// <summary>
    /// Нормализация адреса. Фактический тип payload на целевой Windows версии
    /// должен быть подтверждён elevated-запуском; поддерживаются наиболее частые представления.
    /// </summary>
    internal static bool TryToAddress(object? value, out IPAddress address)
    {
        switch (value)
        {
            case IPAddress direct:
                address = direct;
                return true;
            case uint ipv4:
                address = new IPAddress(BitConverter.GetBytes(ipv4));
                return true;
            case int ipv4 when ipv4 >= 0:
                address = new IPAddress(BitConverter.GetBytes((uint)ipv4));
                return true;
            case byte[] bytes when bytes.Length is 4 or 16:
                address = new IPAddress(bytes);
                return true;
            case string text when IPAddress.TryParse(text, out var parsedAddress):
                address = parsedAddress;
                return true;
            default:
                address = IPAddress.None;
                return false;
        }
    }

    internal static string BuildKey(UdpActivityEvent item) =>
        $"{item.ProcessId}|{item.RemoteAddress.AddressFamily}|{item.RemoteAddress}|{item.RemotePort}|" +
        $"{item.LocalAddress}:{item.LocalPort}";
}
