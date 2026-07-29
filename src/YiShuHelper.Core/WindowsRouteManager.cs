using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;

namespace YiShuHelper;

public sealed class WindowsRouteManager : IRouteManager
{
    private const ushort AfInet = 2;
    private const uint ErrorSuccess = 0;
    private const uint ErrorObjectAlreadyExists = 5010;
    private const uint ErrorNotFound = 1168;

    public IReadOnlyList<RouteEntry> GetRoutes(uint interfaceIndex) =>
        ReadRows().Where(x => x.InterfaceIndex == interfaceIndex).Select(ToRouteEntry).ToList();

    public bool Exists(uint interfaceIndex, Ipv4Prefix prefix) =>
        ReadRows().Any(x => x.InterfaceIndex == interfaceIndex && ToPrefix(x.DestinationPrefix).Equals(prefix));

    public bool Create(uint interfaceIndex, Ipv4Prefix prefix, uint metric = 5)
    {
        var row = new MibIpForwardRow2();
        InitializeIpForwardEntry(ref row);
        row.InterfaceIndex = interfaceIndex;
        row.DestinationPrefix = ToNativePrefix(prefix);
        row.NextHop = ToSockaddr(IPAddress.Any);
        row.Metric = metric;
        var code = CreateIpForwardEntry2(ref row);
        if (code == ErrorSuccess) return true;
        if (code == ErrorObjectAlreadyExists) return false;
        throw new Win32Exception((int)code, $"创建路由 {prefix} 失败。");
    }

    public bool Create(RouteEntry route)
    {
        var prefix = Ipv4Prefix.Parse(route.DestinationPrefix);
        if (Exists(route.InterfaceIndex, prefix)) return false;
        var row = new MibIpForwardRow2();
        InitializeIpForwardEntry(ref row);
        row.InterfaceIndex = route.InterfaceIndex;
        row.DestinationPrefix = ToNativePrefix(prefix);
        row.NextHop = ToSockaddr(IPAddress.Parse(route.NextHop));
        row.Metric = route.Metric;
        var code = CreateIpForwardEntry2(ref row);
        if (code == ErrorSuccess) return true;
        if (code == ErrorObjectAlreadyExists) return false;
        throw new Win32Exception((int)code, $"恢复路由 {route.DestinationPrefix} 失败。");
    }

    public bool Delete(uint interfaceIndex, Ipv4Prefix prefix)
    {
        var row = ReadRows().FirstOrDefault(x => x.InterfaceIndex == interfaceIndex && ToPrefix(x.DestinationPrefix).Equals(prefix));
        if (row.InterfaceIndex == 0) return false;
        var code = DeleteIpForwardEntry2(ref row);
        if (code == ErrorSuccess) return true;
        if (code == ErrorNotFound) return false;
        throw new Win32Exception((int)code, $"删除路由 {prefix} 失败。");
    }

    private static List<MibIpForwardRow2> ReadRows()
    {
        var code = GetIpForwardTable2(AfInet, out var table);
        if (code != ErrorSuccess) throw new Win32Exception((int)code, "读取 Windows IPv4 路由表失败。");
        try
        {
            var count = Marshal.ReadInt32(table);
            var first = IntPtr.Add(table, 8);
            var size = Marshal.SizeOf<MibIpForwardRow2>();
            var rows = new List<MibIpForwardRow2>(count);
            for (var index = 0; index < count; index++)
                rows.Add(Marshal.PtrToStructure<MibIpForwardRow2>(IntPtr.Add(first, index * size)));
            return rows;
        }
        finally
        {
            FreeMibTable(table);
        }
    }

    private static RouteEntry ToRouteEntry(MibIpForwardRow2 row) =>
        new(ToPrefix(row.DestinationPrefix).ToString(), row.InterfaceIndex, ToIPAddress(row.NextHop).ToString(), row.Metric);

    private static Ipv4Prefix ToPrefix(IpAddressPrefix prefix) =>
        new(Ipv4Prefix.ToUInt32(ToIPAddress(prefix.Prefix)), prefix.PrefixLength);

    private static IpAddressPrefix ToNativePrefix(Ipv4Prefix prefix) =>
        new() { Prefix = ToSockaddr(prefix.NetworkAddress), PrefixLength = prefix.PrefixLength };

    private static SockaddrInet ToSockaddr(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return new SockaddrInet
        {
            Family = AfInet,
            Ipv4Address = BitConverter.ToUInt32(bytes, 0)
        };
    }

    private static IPAddress ToIPAddress(SockaddrInet address) =>
        address.Family == AfInet ? new IPAddress(BitConverter.GetBytes(address.Ipv4Address)) : IPAddress.Any;

    [StructLayout(LayoutKind.Explicit, Size = 28)]
    private struct SockaddrInet
    {
        [FieldOffset(0)] public ushort Family;
        [FieldOffset(4)] public uint Ipv4Address;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IpAddressPrefix
    {
        public SockaddrInet Prefix;
        public byte PrefixLength;
    }

    [StructLayout(LayoutKind.Explicit, Size = 104)]
    private struct MibIpForwardRow2
    {
        [FieldOffset(0)] public ulong InterfaceLuid;
        [FieldOffset(8)] public uint InterfaceIndex;
        [FieldOffset(12)] public IpAddressPrefix DestinationPrefix;
        [FieldOffset(44)] public SockaddrInet NextHop;
        [FieldOffset(72)] public byte SitePrefixLength;
        [FieldOffset(76)] public uint ValidLifetime;
        [FieldOffset(80)] public uint PreferredLifetime;
        [FieldOffset(84)] public uint Metric;
        [FieldOffset(88)] public uint Protocol;
        [FieldOffset(92)] public byte Loopback;
        [FieldOffset(93)] public byte AutoconfigureAddress;
        [FieldOffset(94)] public byte Publish;
        [FieldOffset(95)] public byte Immortal;
        [FieldOffset(96)] public uint Age;
        [FieldOffset(100)] public uint Origin;
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetIpForwardTable2(ushort family, out IntPtr table);

    [DllImport("iphlpapi.dll")]
    private static extern void FreeMibTable(IntPtr memory);

    [DllImport("iphlpapi.dll")]
    private static extern void InitializeIpForwardEntry(ref MibIpForwardRow2 row);

    [DllImport("iphlpapi.dll")]
    private static extern uint CreateIpForwardEntry2(ref MibIpForwardRow2 row);

    [DllImport("iphlpapi.dll")]
    private static extern uint DeleteIpForwardEntry2(ref MibIpForwardRow2 row);
}
