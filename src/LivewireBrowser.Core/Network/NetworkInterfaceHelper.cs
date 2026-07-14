using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LivewireBrowser.Core.Network;

public static class NetworkInterfaceHelper
{
    public static List<NetworkInterfaceInfo> GetAvailableInterfaces()
    {
        var result = new List<NetworkInterfaceInfo>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            var props = nic.GetIPProperties();
            foreach (var addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                var ip = addr.Address;
                var mask = addr.IPv4Mask ?? IPAddress.Parse("255.255.255.0");
                var broadcast = ComputeBroadcastAddress(ip, mask);

                result.Add(new NetworkInterfaceInfo(
                    Id: ip.ToString(),
                    DisplayName: $"{nic.Name} ({ip})",
                    IpAddress: ip.ToString(),
                    SubnetMask: mask.ToString(),
                    BroadcastAddress: broadcast.ToString()));
            }
        }

        return result;
    }

    public static NetworkInterfaceInfo? FindByAddress(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return null;

        return GetAvailableInterfaces().FirstOrDefault(i => i.IpAddress == ipAddress);
    }

    /// <summary>
    /// Enumerates host addresses on the subnet of the given interface (excluding network/broadcast),
    /// capped at maxHosts to keep a TCP sweep of a large subnet (e.g. a /16) bounded.
    /// </summary>
    public static List<string> GetHostAddresses(string? interfaceAddress, int maxHosts = 4096)
    {
        var nic = FindByAddress(interfaceAddress);
        if (nic == null)
            return new List<string>();

        return GetHostAddressesForSubnet(
            ToUInt32(IPAddress.Parse(nic.IpAddress).GetAddressBytes()),
            ToUInt32(IPAddress.Parse(nic.SubnetMask).GetAddressBytes()),
            maxHosts);
    }

    /// <summary>
    /// Pure host-enumeration math, split out from GetHostAddresses so it's testable without
    /// depending on the test machine's actual network adapters.
    /// </summary>
    internal static List<string> GetHostAddressesForSubnet(uint ip, uint mask, int maxHosts)
    {
        var network = ip & mask;
        var broadcast = network | ~mask;

        var hosts = new List<string>();
        for (var addr = network + 1; addr < broadcast && hosts.Count < maxHosts; addr++)
            hosts.Add(FromUInt32(addr).ToString());

        return hosts;
    }

    private static uint ToUInt32(byte[] bytes) =>
        (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);

    private static IPAddress FromUInt32(uint value) =>
        new(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });

    private static IPAddress ComputeBroadcastAddress(IPAddress address, IPAddress mask)
    {
        var addressBytes = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        var broadcastBytes = new byte[addressBytes.Length];

        for (var i = 0; i < addressBytes.Length; i++)
            broadcastBytes[i] = (byte)(addressBytes[i] | (byte)~maskBytes[i]);

        return new IPAddress(broadcastBytes);
    }
}
