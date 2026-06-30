using NAudio.Wave;

namespace LivewireBrowser.Audio;

/// <summary>
/// Pass-through ISampleProvider (same pattern as <see cref="LevelMeter"/>) that taps raw
/// interleaved L/R sample pairs for the phasescope display. Only meaningful for stereo
/// (2-channel) sources — on mono or other channel counts it forwards audio unchanged but
/// never raises <see cref="SamplePairsReady"/>, leaving the phasescope idle rather than
/// plotting a single-channel value against itself.
/// </summary>
public class PhaseScopeMeter : ISampleProvider
{
    // Caps how many points one Read() call can push to the UI — a single WASAPI pull can
    // carry hundreds of frames; plotting every one would flood the UI thread for no visual
    // benefit (the phasescope only needs enough points per frame to look like a continuous
    // trace, not every individual sample).
    private const int MaxPointsPerRead = 200;

    private readonly ISampleProvider _source;
    private readonly bool _isStereo;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public event Action<(float Left, float Right)[]>? SamplePairsReady;

    public PhaseScopeMeter(ISampleProvider source)
    {
        _source = source;
        _isStereo = source.WaveFormat.Channels == 2;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = _source.Read(buffer, offset, count);

        if (_isStereo && samplesRead >= 2 && SamplePairsReady != null)
        {
            var frameCount = samplesRead / 2;
            var pointCount = Math.Min(frameCount, MaxPointsPerRead);
            var stride = Math.Max(1, frameCount / pointCount);

            var pairs = new (float Left, float Right)[pointCount];
            for (var i = 0; i < pointCount; i++)
            {
                var frameIndex = i * stride;
                var sampleIndex = offset + frameIndex * 2;
                pairs[i] = (buffer[sampleIndex], buffer[sampleIndex + 1]);
            }

            SamplePairsReady.Invoke(pairs);
        }

        return samplesRead;
    }
}
