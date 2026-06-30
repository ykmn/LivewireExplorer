using NAudio.Wave;

namespace LivewireBrowser.Audio;

public class LevelMeter : ISampleProvider
{
    private readonly ISampleProvider _source;
    public WaveFormat WaveFormat => _source.WaveFormat;

    public float CurrentPeak { get; private set; }

    public event Action<float>? LevelChanged;

    public LevelMeter(ISampleProvider source)
    {
        _source = source;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = _source.Read(buffer, offset, count);

        var peak = 0f;
        for (var i = 0; i < samplesRead; i++)
        {
            var abs = Math.Abs(buffer[offset + i]);
            if (abs > peak)
                peak = abs;
        }

        CurrentPeak = peak;
        LevelChanged?.Invoke(peak);

        return samplesRead;
    }
}
