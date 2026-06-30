using System.Text;
using LivewireBrowser.Core.Discovery;
using Xunit;

namespace LivewireBrowser.Core.Tests;

public class SapListenerTests
{
    private static byte[] BuildSapPacket(string sdpBody)
    {
        var header = new byte[8]; // version/flags, auth len, msg id hash, source address
        var body = Encoding.UTF8.GetBytes(sdpBody);
        return header.Concat(body).ToArray();
    }

    [Fact]
    public void TryParseSapPacket_ExtractsChannelNumberNameAndAddress()
    {
        var sdp = "v=0\r\no=- 1 1 IN IP4 192.168.1.10\r\ns=channel=12 Studio Mic\r\nc=IN IP4 239.192.0.12/32\r\nt=0 0\r\nm=audio 5004 RTP/AVP 96\r\n";
        var packet = BuildSapPacket(sdp);

        var channel = SapListener.TryParseSapPacket(packet);

        Assert.NotNull(channel);
        Assert.Equal(12, channel!.ChannelNumber);
        Assert.Equal("239.192.0.12", channel.MulticastAddress);
        Assert.Equal(5004, channel.Port);
        Assert.Contains("Studio Mic", channel.Name);
    }

    [Fact]
    public void TryParseSapPacket_TooShort_ReturnsNull()
    {
        Assert.Null(SapListener.TryParseSapPacket(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void TryParseSapPacket_MissingConnectionLine_ReturnsNull()
    {
        var sdp = "v=0\r\ns=channel=5 No Connection\r\nm=audio 5004 RTP/AVP 96\r\n";
        var packet = BuildSapPacket(sdp);

        Assert.Null(SapListener.TryParseSapPacket(packet));
    }
}
