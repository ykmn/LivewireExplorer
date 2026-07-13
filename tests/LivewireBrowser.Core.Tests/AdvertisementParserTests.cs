using System.Linq;
using LivewireBrowser.Core.Discovery;
using Xunit;

namespace LivewireBrowser.Core.Tests;

public class AdvertisementParserTests
{
    private static byte[] HexToBytes(string hex) =>
        hex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
           .Select(h => Convert.ToByte(h, 16))
           .ToArray();

    [Fact]
    public void Parse_PeriodicBeacon_HasNoChannelsOrName()
    {
        // Real 87-byte periodic beacon (ADVT=2) captured from a live network.
        var packet = HexToBytes(
            "03 00 02 07 00 58 27 F9 00 00 00 00 00 00 00 00 4E 45 53 54 00 03 50 56 45 52 08 00 02 41 44 56 54 " +
            "07 02 54 45 52 4D 06 00 2D 49 4E 44 49 00 05 41 44 56 56 01 00 00 00 1B 48 57 49 44 08 07 0A 49 4E " +
            "49 50 01 AC 16 07 0A 55 44 50 43 08 0F A0 4E 55 4D 53 08 00 02");

        var parsed = AdvertisementParser.Parse(packet);

        Assert.Null(parsed.DeviceName);
        Assert.Empty(parsed.Channels);
    }

    [Fact]
    public void Parse_FullAdvertisement_ExtractsDeviceNameAndFirstChannel()
    {
        // First part of a real 990-byte "full info" advertisement (ADVT=1) — device
        // "Guest-Engine" at 172.22.21.3, first source "Program 1" on 239.192.82.149.
        var packet = HexToBytes(
            "03 00 02 07 68 65 4F 0C 00 00 00 00 00 00 00 00 4E 45 53 54 00 0B 50 56 45 52 08 00 02 41 44 56 54 " +
            "07 01 54 45 52 4D 06 00 54 49 4E 44 49 00 06 41 44 56 56 01 00 00 05 5B 48 57 49 44 08 15 03 49 4E " +
            "49 50 01 AC 16 15 03 55 44 50 43 08 0F A0 4E 55 4D 53 08 00 1F 41 54 52 4E 03 00 20 " +
            "47 75 65 73 74 2D 45 6E 67 69 6E 65 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 " +
            "53 30 30 31 06 00 65 49 4E 44 49 00 0B 50 53 49 44 01 00 00 52 95 53 48 41 42 07 00 46 53 49 44 01 EF C0 52 95 " +
            "46 41 53 54 07 02 46 41 53 4D 07 01 42 53 49 44 01 EF C1 52 95 42 41 53 54 07 01 42 41 53 4D 07 00 " +
            "4C 50 49 44 01 00 00 52 95 53 54 50 4C 07 00 50 53 4E 4D 03 00 10 50 72 6F 67 72 61 6D 20 31 00 00 00 00 00 00 00");

        var parsed = AdvertisementParser.Parse(packet);

        Assert.Equal("Guest-Engine", parsed.DeviceName);
        var channel = Assert.Single(parsed.Channels);
        Assert.Equal(1, channel.ChannelNumber);
        Assert.Equal(0x5295, channel.LwNumber);
        Assert.Equal("239.192.82.149", channel.MulticastAddress);
        Assert.Equal("Program 1", channel.Name);
        Assert.Equal(5004, channel.Port);
    }

    [Fact]
    public void Parse_SourceWithBusyTag_DoesNotAbortScanOfLaterSources()
    {
        // Synthetic packet (hand-built per the documented TLV grammar, not a live
        // capture): device "Test-Node" (TYPE "NX12"), then a "lightweight" source
        // block (S001: INDI+PSID+BUSY only, no FSID/PSNM — the shape real devices
        // use for a source with no configured name), followed by a normal source
        // block (S002: PSID+FSID+PSNM). Before the 0x09 (BUSY) width fix, hitting
        // BUSY's type byte aborted the scan and S002 was silently never parsed.
        var packet = HexToBytes(
            "03 00 02 07 00 00 00 00 00 00 00 00 00 00 00 00 " +
            "41 54 52 4E 03 00 09 54 65 73 74 2D 4E 6F 64 65 " +
            "54 59 50 45 01 4E 58 31 32 " +
            "53 30 30 31 06 00 65 " +
            "49 4E 44 49 00 01 " +
            "50 53 49 44 01 00 00 00 05 " +
            "42 55 53 59 09 00 00 00 00 00 00 00 00 " +
            "53 30 30 32 06 00 1C " +
            "49 4E 44 49 00 01 " +
            "50 53 49 44 01 00 00 00 06 " +
            "46 53 49 44 01 EF C0 00 06 " +
            "50 53 4E 4D 03 00 06 53 74 75 64 69 6F");

        var parsed = AdvertisementParser.Parse(packet);

        Assert.Equal("Test-Node", parsed.DeviceName);
        Assert.Equal("NX12", parsed.DeviceType);
        Assert.Equal(2, parsed.Channels.Count);

        var lightweight = parsed.Channels[0];
        Assert.Equal(1, lightweight.ChannelNumber);
        Assert.Equal(5, lightweight.LwNumber);
        Assert.Equal("239.192.0.5", lightweight.MulticastAddress);
        Assert.Equal("Channel 1", lightweight.Name); // no PSNM -> fallback name
        Assert.Equal(5004, lightweight.Port);

        var named = parsed.Channels[1];
        Assert.Equal(2, named.ChannelNumber);
        Assert.Equal(6, named.LwNumber);
        Assert.Equal("239.192.0.6", named.MulticastAddress);
        Assert.Equal("Studio", named.Name);
        Assert.Equal(5004, named.Port);
    }
}
