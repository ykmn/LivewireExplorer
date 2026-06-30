using LivewireBrowser.Audio;
using Xunit;

namespace LivewireBrowser.Core.Tests;

public class RtpReceiverTests
{
    [Fact]
    public void TryGetPayloadRange_PlainHeader_ReturnsPayloadAfter12Bytes()
    {
        var packet = BuildPacket(csrcCount: 0, hasExtension: false, hasPadding: 0, payload: new byte[] { 1, 2, 3, 4 });

        var ok = RtpReceiver.TryGetPayloadRange(packet, out var offset, out var length);

        Assert.True(ok);
        Assert.Equal(12, offset);
        Assert.Equal(4, length);
    }

    [Fact]
    public void TryGetPayloadRange_WithCsrcList_SkipsCsrcBytes()
    {
        var packet = BuildPacket(csrcCount: 2, hasExtension: false, hasPadding: 0, payload: new byte[] { 9, 9, 9, 9 });

        var ok = RtpReceiver.TryGetPayloadRange(packet, out var offset, out var length);

        Assert.True(ok);
        Assert.Equal(12 + 2 * 4, offset);
        Assert.Equal(4, length);
    }

    [Fact]
    public void TryGetPayloadRange_WithExtensionHeader_SkipsExtensionBytes()
    {
        var extension = new byte[] { 0xBE, 0xDE, 0x00, 0x02, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 }; // 4-byte ext header + 2 words (8 bytes) of data
        var packet = BuildPacket(csrcCount: 0, hasExtension: true, hasPadding: 0,
            payload: new byte[] { 7, 7, 7, 7 }, extension: extension);

        var ok = RtpReceiver.TryGetPayloadRange(packet, out var offset, out var length);

        Assert.True(ok);
        Assert.Equal(12 + extension.Length, offset);
        Assert.Equal(4, length);
    }

    [Fact]
    public void TryGetPayloadRange_WithPadding_TrimsTrailingPaddingBytes()
    {
        var packet = BuildPacket(csrcCount: 0, hasExtension: false, hasPadding: 2,
            payload: new byte[] { 5, 6, 7, 8, 0, 2 });

        var ok = RtpReceiver.TryGetPayloadRange(packet, out var offset, out var length);

        Assert.True(ok);
        Assert.Equal(12, offset);
        Assert.Equal(4, length); // last 2 bytes (the padding count byte and one more) trimmed
    }

    [Fact]
    public void TryGetPayloadRange_NonRtpVersion_ReturnsFalse()
    {
        var packet = BuildPacket(csrcCount: 0, hasExtension: false, hasPadding: 0, payload: new byte[] { 1, 2 });
        packet[0] = 0x00; // version 0

        var ok = RtpReceiver.TryGetPayloadRange(packet, out _, out _);

        Assert.False(ok);
    }

    private static byte[] BuildPacket(int csrcCount, bool hasExtension, int hasPadding, byte[] payload, byte[]? extension = null)
    {
        var header = new byte[12 + csrcCount * 4 + (hasExtension ? extension!.Length : 0)];

        var b0 = 0x80 | csrcCount; // version 2, no padding bit here (set below)
        if (hasPadding > 0)
            b0 |= 0x20;
        if (hasExtension)
            b0 |= 0x10;
        header[0] = (byte)b0;
        header[1] = 96; // payload type

        if (hasExtension)
            Array.Copy(extension!, 0, header, 12 + csrcCount * 4, extension!.Length);

        var packet = new byte[header.Length + payload.Length];
        Array.Copy(header, packet, header.Length);
        Array.Copy(payload, 0, packet, header.Length, payload.Length);

        if (hasPadding > 0)
            packet[^1] = (byte)hasPadding;

        return packet;
    }
}
