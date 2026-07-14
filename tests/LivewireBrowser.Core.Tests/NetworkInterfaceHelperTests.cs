using System.Net;
using LivewireBrowser.Core.Network;
using Xunit;

namespace LivewireBrowser.Core.Tests;

public class NetworkInterfaceHelperTests
{
    private static uint ToUInt32(string address)
    {
        var bytes = IPAddress.Parse(address).GetAddressBytes();
        return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
    }

    [Fact]
    public void GetHostAddressesForSubnet_DefaultCap_StopsAt4096()
    {
        var ip = ToUInt32("172.22.5.10");
        var mask = ToUInt32("255.255.0.0");

        var hosts = NetworkInterfaceHelper.GetHostAddressesForSubnet(ip, mask, 4096);

        Assert.Equal(4096, hosts.Count);
        Assert.Equal("172.22.0.1", hosts[0]);
        Assert.Equal("172.22.16.0", hosts[^1]);
        Assert.DoesNotContain("172.22.21.21", hosts);
    }

    [Fact]
    public void GetHostAddressesForSubnet_CustomCap_CoversFullSlashSixteen()
    {
        var ip = ToUInt32("172.22.5.10");
        var mask = ToUInt32("255.255.0.0");

        var hosts = NetworkInterfaceHelper.GetHostAddressesForSubnet(ip, mask, 65536);

        Assert.Equal(65534, hosts.Count);
        Assert.Contains("172.22.21.21", hosts);
        Assert.Contains("172.22.22.4", hosts);
    }

    [Fact]
    public void GetHostAddressesForSubnet_CapLargerThanSubnet_StopsAtBroadcast()
    {
        var ip = ToUInt32("192.168.1.10");
        var mask = ToUInt32("255.255.255.0");

        var hosts = NetworkInterfaceHelper.GetHostAddressesForSubnet(ip, mask, 65536);

        Assert.Equal(254, hosts.Count);
        Assert.Equal("192.168.1.1", hosts[0]);
        Assert.Equal("192.168.1.254", hosts[^1]);
    }
}
