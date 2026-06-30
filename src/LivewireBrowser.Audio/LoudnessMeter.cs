using NAudio.Wave;

namespace LivewireBrowser.Audio;

public readonly struct LoudnessSnapshot
{
    /// <summary>Estimated true peak of the most recent 100ms block, in dBTP, combined across
    /// channels (max of TruePeakDbLeft/TruePeakDbRight) — kept for the single shared TP
    /// readout/reset under the two-bar meter; per-channel values drive the bars themselves.</summary>
    public double TruePeakDb { get; init; }
    public double TruePeakDbLeft { get; init; }
    public double TruePeakDbRight { get; init; }
    public double MomentaryLufs { get; init; }
    public double ShortTermLufs { get; init; }
    public double IntegratedLufs { get; init; }
}

/// <summary>
/// Computes ITU-R BS.1770 / EBU R128 style loudness (K-weighted, gated) and an
/// approximate true peak from the audio actually being played (post-volume, same point
/// in the chain as <see cref="LevelMeter"/>). Two simplifications versus a reference
/// meter, both noted where they matter:
///   - True peak uses 4x linear-interpolated oversampling, not a full polyphase
///     reconstruction filter (ITU-R BS.1770 Annex 2) — good enough to catch most
///     inter-sample overs, not a certified measurement.
///   - K-weighting coefficients are for 48 kHz, which is what Livewire always uses
///     (see ChannelInfo.SampleRate) — this class doesn't handle other rates.
/// </summary>
public class LoudnessMeter : ISampleProvider
{
    private const int SampleRate = 48000;
    private const int BlockSizeSamplesPerChannel = SampleRate / 10; // 100 ms gating block
    private const int MomentaryBlocks = 4; // 400 ms
    private const int ShortTermBlocks = 30; // 3000 ms
    private const int MaxIntegratedBlocks = 216_000; // 6 hours of 100ms blocks — bounds memory for long sessions

    // ComputeIntegrated() is an O(n) two-pass gated mean over every block since playback
    // started — fine on a 100ms tick early in a session, but called from Read(), which
    // WasapiOut invokes on its own realtime audio thread. Left running every block, this
    // grows without bound for the length of the session (up to MaxIntegratedBlocks, i.e.
    // 6 hours' worth) and is exactly the kind of unbounded per-callback work that causes
    // audible WASAPI underruns/crackles on long-running channels — see HISTORY.md "Запрос
    // 48". Recomputing once a second instead of every 100ms block cuts that cost ~90%; the
    // "Integrated" reading is a whole-session average anyway, so a 1Hz refresh is not a
    // perceptible regression for the UI.
    private const int IntegratedRecomputeEveryBlocks = 10; // 1000 ms

    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly Biquad[] _preFilters;
    private readonly Biquad[] _rlbFilters;
    private readonly double[] _blockSumSquares;
    private readonly float[] _prevSample;
    private int _blockSampleCount;

    // Per-channel (was a single combined double) — the meter UI now shows True Peak as two
    // independent thin L/R bars instead of one bar for the louder-of-the-two channel.
    private readonly double[] _blockTruePeakLinear;

    // Fixed-size ring buffer instead of Queue<double>+ToArray(): the old AverageOfLast()
    // allocated a fresh array every 100ms block purely to read the last few entries —
    // unnecessary GC pressure on the same realtime audio thread as the O(n) issue above.
    private readonly double[] _recentBlockZ = new double[ShortTermBlocks];
    private int _recentBlockZHead;
    private int _recentBlockZCount;

    private readonly List<double> _integratedBlockZ = new();
    private double _lastIntegratedLufs = double.NegativeInfinity;

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>
    /// Fires once per 100ms gating block. Running "max" values for the UI (reset on click)
    /// are tracked by the subscriber (PlayerViewModel), not in here — this class only
    /// reports the current measurement.
    /// </summary>
    public event Action<LoudnessSnapshot>? Updated;

    public LoudnessMeter(ISampleProvider source)
    {
        _source = source;
        _channels = Math.Max(1, source.WaveFormat.Channels);
        _preFilters = new Biquad[_channels];
        _rlbFilters = new Biquad[_channels];
        for (var c = 0; c < _channels; c++)
        {
            // ITU-R BS.1770-4 K-weighting cascade, coefficients for 48 kHz.
            _preFilters[c] = new Biquad(1.53512485958697f, -2.69169618940638f, 1.19839281085285f,
                -1.69065929318241f, 0.73248077421585f);
            _rlbFilters[c] = new Biquad(1.0f, -2.0f, 1.0f, -1.99004745483398f, 0.99007225036621f);
        }
        _blockSumSquares = new double[_channels];
        _prevSample = new float[_channels];
        _blockTruePeakLinear = new double[_channels];
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = _source.Read(buffer, offset, count);
        ProcessSamples(buffer, offset, samplesRead);
        return samplesRead;
    }

    private void ProcessSamples(float[] buffer, int offset, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var channel = i % _channels;
            var sample = buffer[offset + i];

            var prev = _prevSample[channel];
            for (var k = 1; k <= 4; k++)
            {
                var interpolated = prev + (sample - prev) * (k / 4f);
                var abs = Math.Abs(interpolated);
                if (abs > _blockTruePeakLinear[channel])
                    _blockTruePeakLinear[channel] = abs;
            }
            _prevSample[channel] = sample;

            var filtered = _rlbFilters[channel].Process(_preFilters[channel].Process(sample));
            _blockSumSquares[channel] += (double)filtered * filtered;

            if (channel == _channels - 1)
            {
                _blockSampleCount++;
                if (_blockSampleCount >= BlockSizeSamplesPerChannel)
                    FinishBlock();
            }
        }
    }

    private void FinishBlock()
    {
        double z = 0;
        for (var c = 0; c < _channels; c++)
            z += _blockSumSquares[c] / _blockSampleCount; // channel weight 1.0 for L/R stereo

        Array.Clear(_blockSumSquares);
        _blockSampleCount = 0;

        _recentBlockZ[_recentBlockZHead] = z;
        _recentBlockZHead = (_recentBlockZHead + 1) % ShortTermBlocks;
        if (_recentBlockZCount < ShortTermBlocks)
            _recentBlockZCount++;

        _integratedBlockZ.Add(z);
        if (_integratedBlockZ.Count > MaxIntegratedBlocks)
            _integratedBlockZ.RemoveAt(0);

        var momentaryLufs = ZToLufs(AverageOfLast(MomentaryBlocks));
        var shortTermLufs = ZToLufs(AverageOfLast(ShortTermBlocks));

        if (_integratedBlockZ.Count == 1 || _integratedBlockZ.Count % IntegratedRecomputeEveryBlocks == 0)
            _lastIntegratedLufs = ComputeIntegrated();
        var integratedLufs = _lastIntegratedLufs;

        var truePeakDbLeft = LinearToDb(_blockTruePeakLinear[0]);
        var truePeakDbRight = LinearToDb(_blockTruePeakLinear[_channels > 1 ? 1 : 0]);
        Array.Clear(_blockTruePeakLinear);

        Updated?.Invoke(new LoudnessSnapshot
        {
            TruePeakDb = Math.Max(truePeakDbLeft, truePeakDbRight),
            TruePeakDbLeft = truePeakDbLeft,
            TruePeakDbRight = truePeakDbRight,
            MomentaryLufs = momentaryLufs,
            ShortTermLufs = shortTermLufs,
            IntegratedLufs = integratedLufs,
        });
    }

    private double AverageOfLast(int blocks)
    {
        if (_recentBlockZCount == 0)
            return 0;

        var take = Math.Min(blocks, _recentBlockZCount);
        double sum = 0;
        for (var i = 1; i <= take; i++)
        {
            var idx = (_recentBlockZHead - i + ShortTermBlocks) % ShortTermBlocks;
            sum += _recentBlockZ[idx];
        }
        return sum / take;
    }

    /// <summary>Two-pass gated mean per EBU R128: absolute gate at -70 LUFS, then a relative
    /// gate 10 LU below the absolute-gated mean.</summary>
    private double ComputeIntegrated()
    {
        if (_integratedBlockZ.Count == 0)
            return double.NegativeInfinity;

        const double absoluteGateLufs = -70.0;

        double sum1 = 0;
        var count1 = 0;
        foreach (var z in _integratedBlockZ)
        {
            if (z > 0 && ZToLufs(z) > absoluteGateLufs)
            {
                sum1 += z;
                count1++;
            }
        }
        if (count1 == 0)
            return double.NegativeInfinity;

        var relativeGateLufs = ZToLufs(sum1 / count1) - 10.0;

        double sum2 = 0;
        var count2 = 0;
        foreach (var z in _integratedBlockZ)
        {
            if (z > 0 && ZToLufs(z) > relativeGateLufs)
            {
                sum2 += z;
                count2++;
            }
        }

        return count2 == 0 ? double.NegativeInfinity : ZToLufs(sum2 / count2);
    }

    private static double ZToLufs(double z) => z <= 0 ? double.NegativeInfinity : -0.691 + 10.0 * Math.Log10(z);
    private static double LinearToDb(double linear) => linear <= 0 ? double.NegativeInfinity : 20.0 * Math.Log10(linear);
}
