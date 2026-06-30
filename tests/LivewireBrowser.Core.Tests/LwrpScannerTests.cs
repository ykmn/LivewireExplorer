using LivewireBrowser.Core.Discovery;
using Xunit;

namespace LivewireBrowser.Core.Tests;

public class LwrpScannerTests
{
    [Fact]
    public void ParseSourceChannels_ExtractsNumberNameAndAddress()
    {
        var lines = new List<string>
        {
            "SRC 1 PSNM:Studio Mic RTPA:239.192.0.1:5004",
            "SRC 2 PSNM:Phone Hybrid RTPA:239.192.0.2",
        };

        var channels = LwrpScanner.ParseSourceChannels(lines);

        Assert.Equal(2, channels.Count);
        Assert.Equal(1, channels[0].ChannelNumber);
        Assert.Equal("Studio Mic", channels[0].Name);
        Assert.Equal("239.192.0.1", channels[0].MulticastAddress);
        Assert.Equal(5004, channels[0].Port);

        Assert.Equal(2, channels[1].ChannelNumber);
        Assert.Equal("239.192.0.2", channels[1].MulticastAddress);
        Assert.Equal(5004, channels[1].Port); // default Livewire RTP port when not specified
    }

    [Fact]
    public void ParseSourceChannels_SkipsLinesWithoutAddress()
    {
        var lines = new List<string> { "SRC 3 PSNM:No Address" };

        var channels = LwrpScanner.ParseSourceChannels(lines);

        Assert.Empty(channels);
    }

    [Fact]
    public void ParseSourceChannels_IgnoresNonSrcLines()
    {
        var lines = new List<string> { "VER DEVID:Element NUMSRC:8", "ERROR 1000 bad command" };

        var channels = LwrpScanner.ParseSourceChannels(lines);

        Assert.Empty(channels);
    }

    [Fact]
    public void ParseSourceChannels_HandlesQuotedNamesWithSpaces()
    {
        var lines = new List<string> { "SRC 4 PSNM:\"Studio A Mic\" RTPA:239.192.0.4:5004" };

        var channels = LwrpScanner.ParseSourceChannels(lines);

        Assert.Single(channels);
        Assert.Equal("Studio A Mic", channels[0].Name);
        Assert.Equal("239.192.0.4", channels[0].MulticastAddress);
    }

    [Fact]
    public void Tokenize_KeepsQuotedPhraseAsSingleToken()
    {
        var tokens = LwrpScanner.Tokenize("MODE:\"Analog Node\" DEVID:xN200");

        Assert.Equal(new[] { "MODE:Analog Node", "DEVID:xN200" }, tokens);
    }

    [Fact]
    public void ParseSourceChannels_ComputesLwNumberFromCanonicalAddress()
    {
        var lines = new List<string> { "SRC 1 PSNM:\"EP+0\" RTPA:\"239.192.36.215\"" };

        var channels = LwrpScanner.ParseSourceChannels(lines);

        Assert.Equal(36 * 256 + 215, channels[0].LwNumber);
    }

    [Theory]
    [InlineData("239.192.0.33", 33)]
    [InlineData("239.192.36.215", 9431)]
    [InlineData("0.0.0.0", 0)]
    [InlineData("224.0.0.1", 0)]
    public void ComputeLwNumber_DecodesCanonicalLivewireBlockOnly(string address, int expected)
    {
        Assert.Equal(expected, LwrpScanner.ComputeLwNumber(address));
    }

    [Fact]
    public void ParseSourceChannels_Rtpe0_MarksChannelInactive()
    {
        var lines = new List<string> { "SRC 1 PSNM:\"SRC 1\" RTPE:0 RTPA:\"239.192.0.1\"" };

        var channels = LwrpScanner.ParseSourceChannels(lines);

        Assert.False(channels[0].IsActive);
    }

    [Theory]
    [InlineData("SRC 1 PSNM:\"FMSport\" RTPE:1 RTPA:\"239.192.39.22\"", true)]
    [InlineData("SRC 1 PSNM:\"Legacy\" RTPA:\"239.192.0.1\"", true)] // RTPE absent -> assume active (older firmware)
    public void ParseSourceChannels_Rtpe1OrAbsent_MarksChannelActive(string line, bool expected)
    {
        var channels = LwrpScanner.ParseSourceChannels(new List<string> { line });

        Assert.Equal(expected, channels[0].IsActive);
    }

    [Fact]
    public void ExtractIpHostname_ParsesLowercaseHostnameAttribute()
    {
        var lines = new List<string> { "IP address:172.22.0.18 netmask:255.255.0.0 gateway:172.22.0.1 hostname:Anlg-serv-18" };

        var hostname = LwrpScanner.ExtractIpHostname(lines);

        Assert.Equal("Anlg-serv-18", hostname);
    }

    [Fact]
    public void ExtractIpHostname_NoHostnameAttribute_ReturnsNull()
    {
        var lines = new List<string> { "IP address:172.22.0.18 netmask:255.255.0.0" };

        Assert.Null(LwrpScanner.ExtractIpHostname(lines));
    }

    [Fact]
    public void ExtractIpHostname_EmptyResponse_ReturnsNull()
    {
        Assert.Null(LwrpScanner.ExtractIpHostname(new List<string>()));
    }

    [Fact]
    public void ExtractIpHostname_ParsesBareHostnameWithoutColon()
    {
        // Confirmed on a real Axia IP-Audio Driver: the IP query reply arrives as two
        // separate lines, and unlike ADDR/LINK this one has no colon at all.
        var lines = new List<string>
        {
            "IP ADDR:\"172.22.0.36\" LINK:1",
            "IP hostname air-dsp2",
        };

        Assert.Equal("air-dsp2", LwrpScanner.ExtractIpHostname(lines));
    }
}
