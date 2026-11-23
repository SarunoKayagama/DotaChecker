using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace DotaChecker.Network.Routing;

/// <summary>
/// Определяет интерфейс, который Windows выберет для адреса назначения.
/// Это данные таблицы маршрутизации, а не доказательство на уровне пакетов.
/// </summary>
public sealed class RouteInterfaceResolver
{
    public RouteInterfaceInfo Resolve(IPAddress destination)
    {
        if (destination.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return new RouteInterfaceInfo(0, string.Empty, "IPv6 пока не поддерживается.");
        }

        var address = BitConverter.ToUInt32(destination.GetAddressBytes(), 0);
        var result = GetBestInterface(address, out var index);
        if (result != 0)
        {
            return new RouteInterfaceInfo(0, string.Empty, $"GetBestInterface error {result}");
        }

        var name = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(item => item.GetIPProperties().GetIPv4Properties()?.Index == (int)index)?.Name
            ?? $"if{index}";

        return new RouteInterfaceInfo((int)index, name, "Выбрано таблицей маршрутизации Windows (не packet-level proof).");
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetBestInterface(uint address, out uint index);
}

public sealed record RouteInterfaceInfo(int Index, string Name, string Note);
