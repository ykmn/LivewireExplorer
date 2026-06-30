using LivewireBrowser.Core.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LivewireBrowser.Audio;

public class AudioPlaybackEngine : IDisposable
{
    private WasapiOut? _output;
    private VolumeSampleProvider? _volumeProvider;
    private LevelMeter? _levelMeter;
    private PhaseScopeMeter? _phaseScopeMeter;
    private LoudnessMeter? _loudnessMeter;

    public event Action<float>? LevelChanged;
    public event Action<LoudnessSnapshot>? LoudnessChanged;
    public event Action<(float Left, float Right)[]>? PhasePairsReceived;

    public static IReadOnlyList<MMDevice> EnumerateOutputDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
    }

    /// <summary>
    /// startPlaybackImmediately=false sets up the device/format/meter chain and calls
    /// WasapiOut.Init() but doesn't call Play() yet — used by ChannelPlayer to pre-buffer a
    /// few hundred ms of audio before WASAPI starts pulling. Without that, Play() was being
    /// called the instant a channel was clicked, before a single RTP packet had arrived (the
    /// receiver only joins the multicast group afterwards), so the BufferedWaveProvider
    /// never had any cushion and stayed pinned near-empty (~10-20ms) for the entire session —
    /// confirmed by the buffer-health log this same investigation added — leaving essentially
    /// no margin to absorb network jitter before an underrun became an audible crackle.
    /// Call Resume() once the caller's pre-buffer target is reached.
    /// </summary>
    public void Start(BufferedWaveProvider waveProvider, string? deviceId, float initialVolume, bool startPlaybackImmediately = true)
    {
        Stop();

        try
        {
            // Metering taps the signal pre-fader (before VolumeSampleProvider) so the level/
            // loudness indicators show what the channel is actually carrying, not what the
            // volume slider currently happens to be set to — moving the slider must not make
            // a hot signal look quiet or vice versa.
            var sampleProvider = waveProvider.ToSampleProvider();
            _levelMeter = new LevelMeter(sampleProvider);
            _levelMeter.LevelChanged += level => LevelChanged?.Invoke(level);
            _phaseScopeMeter = new PhaseScopeMeter(_levelMeter);
            _phaseScopeMeter.SamplePairsReady += pairs => PhasePairsReceived?.Invoke(pairs);
            _loudnessMeter = new LoudnessMeter(_phaseScopeMeter);
            _loudnessMeter.Updated += snapshot => LoudnessChanged?.Invoke(snapshot);
            _volumeProvider = new VolumeSampleProvider(_loudnessMeter) { Volume = initialVolume };

            MMDevice? device = null;
            if (!string.IsNullOrEmpty(deviceId))
            {
                using var enumerator = new MMDeviceEnumerator();
                device = enumerator.GetDevice(deviceId);
            }

            Log.Info($"AudioPlaybackEngine: starting playback on device '{device?.FriendlyName ?? "default"}', format {waveProvider.WaveFormat}");

            // 200ms (up from 100ms) — virtual/kernel-streaming WDM devices (e.g. the Axia
            // IP-Audio Driver outputs) are more prone to WASAPI shared-mode scheduling
            // jitter than native endpoints; a tighter target latency left less margin
            // before an underrun became an audible crackle. Costs 100ms more time before
            // a freshly clicked channel is heard — not significant for monitoring audio,
            // not a sync-critical use case.
            _output = device != null
                ? new WasapiOut(device, AudioClientShareMode.Shared, true, 200)
                : new WasapiOut(AudioClientShareMode.Shared, 200);

            _output.Init(_volumeProvider);

            // WASAPI shared mode is allowed to silently substitute the device's own mix
            // format for whatever we requested (e.g. if the endpoint's current Windows
            // Sound Control Panel format is mono, or a different sample rate) — NAudio
            // does not throw in that case, it just resamples/remixes internally. Logging
            // the format actually negotiated, next to the one we asked for, is the only way
            // to tell a real mono/stereo bug in our own pipeline apart from a Windows-side
            // device configuration that's silently downmixing on the way out.
            Log.Info($"AudioPlaybackEngine: negotiated output format {_output.OutputWaveFormat}");

            if (startPlaybackImmediately)
                _output.Play();
        }
        catch (Exception ex)
        {
            Log.Error("AudioPlaybackEngine: failed to start playback", ex);
            throw;
        }
    }

    /// <summary>Starts WASAPI pulling from the provider after a deferred Start(..., startPlaybackImmediately: false).
    /// No-op if Stop() already ran (e.g. the user switched channels again before the pre-buffer target was reached).</summary>
    public void Resume()
    {
        if (_output == null)
            return;

        Log.Info("AudioPlaybackEngine: pre-buffer ready, starting playback");
        _output.Play();
    }

    public void SetVolume(float volume)
    {
        if (_volumeProvider != null)
            _volumeProvider.Volume = Math.Clamp(volume, 0f, 1f);
    }

    public void Stop()
    {
        if (_output != null)
            Log.Info("AudioPlaybackEngine: stopping playback");

        _output?.Stop();
        _output?.Dispose();
        _output = null;
        _volumeProvider = null;
        _levelMeter = null;
        _phaseScopeMeter = null;
        _loudnessMeter = null;
    }

    public void Dispose() => Stop();
}
