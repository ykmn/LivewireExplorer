using LivewireBrowser.Audio;
using Xunit;

namespace LivewireBrowser.Core.Tests;

public class LivewireAudioDecoderTests
{
    [Fact]
    public void Feed_24BitBigEndian_SwapsToLittleEndianTripletsAndPreservesLength()
    {
        var decoder = new LivewireAudioDecoder(sampleRate: 48000, channels: 2, bitsPerSample: 24);

        // Two 24-bit big-endian samples: 0x000005 and 0xFFFFD9 (as seen in a real capture).
        var payload = new byte[] { 0x00, 0x00, 0x05, 0xFF, 0xFF, 0xD9 };

        decoder.Feed(payload);

        Assert.Equal(6, decoder.Provider.BufferedBytes);
        var buffered = new byte[6];
        decoder.Provider.Read(buffered, 0, 6);
        Assert.Equal(new byte[] { 0x05, 0x00, 0x00, 0xD9, 0xFF, 0xFF }, buffered);
    }

    [Fact]
    public void Feed_16Bit_SwapsToLittleEndianPairs()
    {
        var decoder = new LivewireAudioDecoder(sampleRate: 48000, channels: 2, bitsPerSample: 16);

        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        decoder.Feed(payload);

        var buffered = new byte[4];
        decoder.Provider.Read(buffered, 0, 4);
        Assert.Equal(new byte[] { 0x02, 0x01, 0x04, 0x03 }, buffered);
    }

    [Fact]
    public void Feed_PayloadNotMultipleOfSampleWidth_TrimsTrailingPartialSample()
    {
        var decoder = new LivewireAudioDecoder(sampleRate: 48000, channels: 2, bitsPerSample: 24);

        var payload = new byte[] { 0x00, 0x00, 0x05, 0xAA }; // 4 bytes, not a multiple of 3

        decoder.Feed(payload);

        Assert.Equal(3, decoder.Provider.BufferedBytes);
    }
}
