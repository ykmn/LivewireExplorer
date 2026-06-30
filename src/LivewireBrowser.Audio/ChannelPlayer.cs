using LivewireBrowser.Core.Logging;

namespace LivewireBrowser.Audio;

/// <summary>
/// Facade tying together RtpReceiver, LivewireAudioDecoder and AudioPlaybackEngine
/// to play a single Livewire channel at a time.
/// </summary>
public class ChannelPlayer : IDisposable
{
    // Target cushion built up before WASAPI starts pulling — see AudioPlaybackEngine.Start's
    // startPlaybackImmediately doc comment for why this is needed at all. Comfortably above
    // both LivewireAudioDecoder's 0.2s "buffer low" warning threshold and gives real margin
    // to absorb jitter, without making a freshly clicked channel feel sluggish to start.
    private static readonly TimeSpan PreBufferTarget = TimeSpan.FromMilliseconds(300);

    // Safety cap: if a stream never delivers (dead source, wrong multicast group, etc.) this
    // stops the pre-buffer wait from blocking playback forever — WASAPI starts anyway and
    // just plays silence/whatever trickles in, same as before this change.
    private static readonly TimeSpan PreBufferTimeout = TimeSpan.FromSeconds(1);

    private readonly RtpReceiver _receiver = new();
    private readonly AudioPlaybackEngine _engine = new();
    private LivewireAudioDecoder? _decoder;

    // Bumped on every Play()/Stop() so a pre-buffer wait left over from a previous (now
    // superseded) channel can tell it's stale and not call Resume() at the wrong time —
    // e.g. when the user clicks through channels quickly.
    private int _playGeneration;

    public event Action<float>? LevelChanged
    {
        add => _engine.LevelChanged += value;
        remove => _engine.LevelChanged -= value;
    }

    public event Action<LoudnessSnapshot>? LoudnessChanged
    {
        add => _engine.LoudnessChanged += value;
        remove => _engine.LoudnessChanged -= value;
    }

    public event Action<(float Left, float Right)[]>? PhasePairsReceived
    {
        add => _engine.PhasePairsReceived += value;
        remove => _engine.PhasePairsReceived -= value;
    }

    public ChannelPlayer()
    {
        _receiver.PayloadReceived += payload => _decoder?.Feed(payload);
    }

    public void Play(string multicastAddress, int port, int sampleRate, int channels, int bitsPerSample,
        string? outputDeviceId, float volume, string? localInterfaceAddress = null)
    {
        Stop();
        var generation = _playGeneration;

        _decoder = new LivewireAudioDecoder(sampleRate, channels, bitsPerSample);
        _engine.Start(_decoder.Provider, outputDeviceId, volume, startPlaybackImmediately: false);
        _receiver.Start(multicastAddress, port, localInterfaceAddress);

        _ = PreBufferThenResumeAsync(_decoder.Provider, generation);
    }

    private async Task PreBufferThenResumeAsync(NAudio.Wave.BufferedWaveProvider provider, int generation)
    {
        try
        {
            var deadline = DateTime.UtcNow + PreBufferTimeout;
            while (provider.BufferedDuration < PreBufferTarget && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10).ConfigureAwait(false);
                if (generation != _playGeneration)
                    return; // superseded by a newer Play()/Stop() — let that one own Resume()
            }
        }
        catch (Exception ex)
        {
            Log.Error("ChannelPlayer: pre-buffer wait failed", ex);
        }

        if (generation == _playGeneration)
            _engine.Resume();
    }

    public void SetVolume(float volume) => _engine.SetVolume(volume);

    public void Stop()
    {
        _playGeneration++;
        _receiver.Stop();
        _engine.Stop();
        _decoder = null;
    }

    public void Dispose()
    {
        Stop();
        _receiver.Dispose();
        _engine.Dispose();
    }
}
