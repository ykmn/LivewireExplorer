using LivewireBrowser.Core.Network;
using Xunit;

namespace LivewireBrowser.Core.Tests;

public class IpAddressUtilTests
{
    [Theory]
    [InlineData("172.22.0.9", "172.22.0.10")]
    [InlineData("10.0.0.1", "10.0.0.2")]
    [InlineData("192.168.1.255", "192.168.2.0")]
    public void ToSortKey_OrdersNumericallyNotLexicographically(string lower, string higher)
    {
        Assert.True(IpAddressUtil.ToSortKey(lower) < IpAddressUtil.ToSortKey(higher));
    }

    [Fact]
    public void ToSortKey_SortsAListOfAddressesNumerically()
    {
        var addresses = new[] { "172.22.0.10", "172.22.0.2", "172.22.0.9", "172.22.1.1" };

        var sorted = addresses.OrderBy(IpAddressUtil.ToSortKey).ToArray();

        Assert.Equal(new[] { "172.22.0.2", "172.22.0.9", "172.22.0.10", "172.22.1.1" }, sorted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-ip")]
    [InlineData("1.2.3")]
    [InlineData("1.2.3.4.5")]
    public void ToSortKey_MalformedInput_ReturnsZeroInsteadOfThrowing(string ip)
    {
        Assert.Equal(0, IpAddressUtil.ToSortKey(ip));
    }
}
