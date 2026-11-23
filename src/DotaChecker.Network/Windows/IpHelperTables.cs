using System.Net;
using System.Runtime.InteropServices;

namespace DotaChecker.Network.Windows;

/// <summary>Чтение расширенных таблиц TCP/UDP через IP Helper API (без запуска netstat).</summary>
internal static class IpHelperTables
{
    private const int OwningModuleSize = 16;

    private delegate uint TableReader(IntPtr buffer, ref uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerModule
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningPid;
        public long CreateTimestamp;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = OwningModuleSize)]
        public ulong[] OwningModuleInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpTableOwnerModule
    {
        public uint NumberOfEntries;
        public MibTcpRowOwnerModule Table;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpRowOwnerModule
    {
        public uint LocalAddress;
        public uint LocalPort;
        public uint OwningPid;
        public long CreateTimestamp;
        public int Flags;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = OwningModuleSize)]
        public ulong[] OwningModuleInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpTableOwnerModule
    {
        public uint NumberOfEntries;
        public MibUdpRowOwnerModule Table;
    }

    internal readonly record struct RawTcpRow(
        TcpState State,
        IPAddress LocalAddress,
        int LocalPort,
        IPAddress RemoteAddress,
        int RemotePort,
        int ProcessId,
        DateTimeOffset CreatedAt,
        bool HasCreationTime);

    internal readonly record struct RawUdpRow(
        IPAddress LocalAddress,
        int LocalPort,
        int ProcessId,
        DateTimeOffset CreatedAt,
        bool HasCreationTime);

    internal static IReadOnlyList<RawTcpRow> ReadTcp()
    {
        var buffer = AllocateTable(static (IntPtr handle, ref uint size) =>
            NativeMethods.GetExtendedTcpTable(handle, ref size, false, NativeMethods.AF_INET,
                NativeMethods.TcpTableClass.TcpTableOwnerModuleAll, 0));

        try
        {
            var tableOffset = (int)Marshal.OffsetOf<MibTcpTableOwnerModule>(nameof(MibTcpTableOwnerModule.Table));
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerModule>();
            var count = (int)Marshal.ReadInt32(buffer, 0);
            var rows = new List<RawTcpRow>(count);

            for (var i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerModule>(IntPtr.Add(buffer, tableOffset + i * rowSize));
                rows.Add(new RawTcpRow(
                    MapState(row.State),
                    ToAddress(row.LocalAddress),
                    ToPort(row.LocalPort),
                    ToAddress(row.RemoteAddress),
                    ToPort(row.RemotePort),
                    ToProcessId(row.OwningPid),
                    ToTimestamp(row.CreateTimestamp, out var hasTime),
                    hasTime));
            }

            return rows;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static IReadOnlyList<RawUdpRow> ReadUdp()
    {
        var buffer = AllocateTable(static (IntPtr handle, ref uint size) =>
            NativeMethods.GetExtendedUdpTable(handle, ref size, false, NativeMethods.AF_INET,
                NativeMethods.UdpTableClass.UdpTableOwnerModule, 0));

        try
        {
            var tableOffset = (int)Marshal.OffsetOf<MibUdpTableOwnerModule>(nameof(MibUdpTableOwnerModule.Table));
            var rowSize = Marshal.SizeOf<MibUdpRowOwnerModule>();
            var count = (int)Marshal.ReadInt32(buffer, 0);
            var rows = new List<RawUdpRow>(count);

            for (var i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MibUdpRowOwnerModule>(IntPtr.Add(buffer, tableOffset + i * rowSize));
                rows.Add(new RawUdpRow(
                    ToAddress(row.LocalAddress),
                    ToPort(row.LocalPort),
                    ToProcessId(row.OwningPid),
                    ToTimestamp(row.CreateTimestamp, out var hasTime),
                    hasTime));
            }

            return rows;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IntPtr AllocateTable(TableReader read)
    {
        uint size = 0;
        var probe = read(IntPtr.Zero, ref size);
        if (probe != NativeMethods.ERROR_INSUFFICIENT_BUFFER)
        {
            throw new InvalidOperationException($"IP Helper table probe failed with code {probe}.");
        }

        var buffer = Marshal.AllocHGlobal(checked((int)size));
        var status = read(buffer, ref size);
        if (status != NativeMethods.NO_ERROR)
        {
            Marshal.FreeHGlobal(buffer);
            throw new InvalidOperationException($"IP Helper table read failed with code {status}.");
        }

        return buffer;
    }

    private static int ToProcessId(uint pid) => pid > int.MaxValue ? 0 : (int)pid;

    private static IPAddress ToAddress(uint networkOrderAddress) =>
        new(BitConverter.GetBytes(networkOrderAddress));

    private static int ToPort(uint networkOrderPort) =>
        IPAddress.NetworkToHostOrder(unchecked((short)networkOrderPort)) & 0xFFFF;

    private static DateTimeOffset ToTimestamp(long timestamp, out bool hasValue)
    {
        if (timestamp == 0)
        {
            hasValue = false;
            return DateTimeOffset.MinValue;
        }

        hasValue = true;
        try
        {
            return DateTimeOffset.FromFileTime(timestamp);
        }
        catch (ArgumentOutOfRangeException)
        {
            hasValue = false;
            return DateTimeOffset.MinValue;
        }
    }

    private static TcpState MapState(uint state) => state switch
    {
        1 => TcpState.Closed,
        2 => TcpState.Listen,
        3 => TcpState.SynSent,
        4 => TcpState.SynReceived,
        5 => TcpState.Established,
        6 => TcpState.FinWait1,
        7 => TcpState.FinWait2,
        8 => TcpState.CloseWait,
        9 => TcpState.Closing,
        10 => TcpState.LastAck,
        11 => TcpState.TimeWait,
        12 => TcpState.DeleteTcb,
        _ => TcpState.Unknown,
    };

    internal enum TcpState
    {
        Unknown,
        Closed,
        Listen,
        SynSent,
        SynReceived,
        Established,
        FinWait1,
        FinWait2,
        CloseWait,
        Closing,
        LastAck,
        TimeWait,
        DeleteTcb,
    }
}
