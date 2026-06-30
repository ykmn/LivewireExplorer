using LivewireBrowser.Core.Logging;
using NAudio.Wave;

namespace LivewireBrowser.Audio;

/// <summary>
/// Converts Livewire RTP payload (linear PCM, network byte order / big-endian)
/// into a NAudio BufferedWaveProvider feed (little-endian, as WASAPI expects).
/// Bytes-per-sample is derived from <c>bitsPerSample</c> rather than hardcoded to 2:
/// a real packet capture confirmed Livewire's standard stream is 24-bit (3 bytes/sample),
/// not 16-bit, and a fixed 2-byte swap silently scrambles 3-byte samples into noise.
/// </summary>
public class LivewireAudioDecoder
{
    // BufferedWaveProvider.DiscardOnBufferOverflow silently drops samples with no log line
    // of its own, and a starved buffer (WASAPI pulling faster than the network is
    // delivering) is just as silent — both are exactly the kind of thing that would explain
    // a user-reported "crackling" complaint without a clear root cause in the logs. Sampled
    // (not logged on every Feed — that's per-packet, ~every few ms) at most once a second so
    // a real problem still shows up promptly without flooding the log file.
    private static readonly TimeSpan BufferHealthLogInterval = TimeSpan.FromSeconds(1);
    private const double LowBufferWarningSeconds = 0.2;

    private readonly int _bytesPerSample;
    private DateTime _lastBufferHealthLog = DateTime.MinValue;

    public WaveFormat Format { get; }
    public BufferedWaveProvider Provider { get; }

    public LivewireAudioDecoder(int sampleRate, int channels, int bitsPerSample)
    {
        _bytesPerSample = bitsPerSample / 8;
        Format = new WaveFormat(sampleRate, bitsPerSample, channels);
        Provider = new BufferedWaveProvider(Format)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(2),
        };
    }

    public void Feed(byte[] rtpPayload)
    {
        if (_bytesPerSample < 1 || rtpPayload.Length < _bytesPerSample)
            return;

        var usableLength = rtpPayload.Length - rtpPayload.Length % _bytesPerSample;
        var littleEndian = new byte[usableLength];
        for (var i = 0; i < usableLength; i += _bytesPerSample)
        {
            for (var b = 0; b < _bytesPerSample; b++)
                littleEndian[i + b] = rtpPayload[i + _bytesPerSample - 1 - b];
        }

        LogBufferHealthIfDue();

        var wouldOverflow = Provider.BufferedBytes + littleEndian.Length > Provider.BufferLength;
        if (wouldOverflow)
            Log.Debug($"LivewireAudioDecoder: buffer full ({Provider.BufferedDuration.TotalMilliseconds:0}ms buffered) — discarding incoming samples");

        Provider.AddSamples(littleEndian, 0, littleEndian.Length);
    }

    private void LogBufferHealthIfDue()
    {
        var now = DateTime.UtcNow;
        if (now - _lastBufferHealthLog < BufferHealthLogInterval)
            return;
        _lastBufferHealthLog = now;

        var bufferedSeconds = Provider.BufferedDuration.TotalSeconds;
        if (bufferedSeconds < LowBufferWarningSeconds)
            Log.Debug($"LivewireAudioDecoder: buffer low ({bufferedSeconds:0.00}s buffered) — risk of WASAPI underrun");
    }
}
