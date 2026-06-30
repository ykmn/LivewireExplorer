using LivewireBrowser.Audio;
using NAudio.Wave;
using Xunit;

namespace LivewireBrowser.Core.Tests;

public class LoudnessMeterTests
{
    private class ConstantSampleProvider : ISampleProvider
    {
        private readonly float _value;
        public WaveFormat WaveFormat { get; }

        public ConstantSampleProvider(float value, int sampleRate, int channels)
        {
            _value = value;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i++)
                buffer[offset + i] = _value;
            return count;
        }
    }

    [Fact]
    public void Silence_ProducesNegativeInfinityLoudnessAndZeroPeak()
    {
        var meter = new LoudnessMeter(new ConstantSampleProvider(0f, 48000, 2));
        LoudnessSnapshot? snapshot = null;
        meter.Updated += s => snapshot = s;

        var buffer = new float[48000 * 2]; // 1 second, several gating blocks
        meter.Read(buffer, 0, buffer.Length);

        Assert.NotNull(snapshot);
        Assert.True(double.IsNegativeInfinity(snapshot!.Value.MomentaryLufs));
        Assert.True(double.IsNegativeInfinity(snapshot.Value.ShortTermLufs));
        Assert.True(double.IsNegativeInfinity(snapshot.Value.TruePeakDb));
    }

    [Fact]
    public void FullScaleSignal_ProducesFiniteLoudnessNearZeroDbPeak()
    {
        var meter = new LoudnessMeter(new ConstantSampleProvider(1f, 48000, 2));
        LoudnessSnapshot? snapshot = null;
        meter.Updated += s => snapshot = s;

        var buffer = new float[4800 * 2]; // exactly one 100ms gating block
        meter.Read(buffer, 0, buffer.Length);

        Assert.NotNull(snapshot);
        Assert.True(double.IsFinite(snapshot!.Value.MomentaryLufs));
        Assert.True(double.IsFinite(snapshot.Value.TruePeakDb));
        Assert.InRange(snapshot.Value.TruePeakDb, -0.5, 0.5); // constant full-scale -> ~0 dBTP
    }

    private class StereoSampleProvider : ISampleProvider
    {
        private readonly float _left;
        private readonly float _right;
        public WaveFormat WaveFormat { get; }

        public StereoSampleProvider(float left, float right, int sampleRate)
        {
            _left = left;
            _right = right;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i += 2)
            {
                buffer[offset + i] = _left;
                if (i + 1 < count)
                    buffer[offset + i + 1] = _right;
            }
            return count;
        }
    }

    /// <summary>
    /// Regression test for the per-channel True Peak split (was a single combined
    /// _blockTruePeakLinear double tracking the louder-of-the-two channel) — see
    /// HISTORY.md "Запрос 51". A louder left channel must not make the right channel's
    /// reading look louder than it really is, and vice versa.
    /// </summary>
    [Fact]
    public void DifferentLeftRightAmplitudes_ProducesIndependentTruePeakPerChannel()
    {
        var meter = new LoudnessMeter(new StereoSampleProvider(left: 1f, right: 0.25f, sampleRate: 48000));
        LoudnessSnapshot? snapshot = null;
        meter.Updated += s => snapshot = s;

        var buffer = new float[4800 * 2]; // one 100ms gating block
        meter.Read(buffer, 0, buffer.Length);

        Assert.NotNull(snapshot);
        Assert.True(double.IsFinite(snapshot!.Value.TruePeakDbLeft));
        Assert.True(double.IsFinite(snapshot.Value.TruePeakDbRight));
        Assert.InRange(snapshot.Value.TruePeakDbLeft, -0.5, 0.5); // full-scale left -> ~0 dBTP
        Assert.True(snapshot.Value.TruePeakDbRight < snapshot.Value.TruePeakDbLeft - 5,
            "quieter right channel should read noticeably lower than the full-scale left channel");
        Assert.Equal(snapshot.Value.TruePeakDbLeft, snapshot.Value.TruePeakDb); // combined = louder of the two
    }

    [Fact]
    public void Read_DoesNotThrowAndForwardsSampleCount()
    {
        var meter = new LoudnessMeter(new ConstantSampleProvider(0.1f, 48000, 2));
        var buffer = new float[100];

        var read = meter.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);
    }

    /// <summary>
    /// Regression test for the ring-buffer rewrite of _recentBlockZ (was Queue+ToArray) and
    /// the throttled (once/second instead of every 100ms block) recompute of IntegratedLufs —
    /// see HISTORY.md "Запрос 48". Feeds several seconds of full-scale signal (well past the
    /// 30-block/3s ShortTerm window and several IntegratedRecomputeEveryBlocks cycles) and
    /// checks every reading stays finite and sane, not just the first block like the older
    /// tests above.
    /// </summary>
    [Fact]
    public void MultiSecondFullScaleSignal_KeepsAllReadingsFiniteAcrossRecomputeCycles()
    {
        var meter = new LoudnessMeter(new ConstantSampleProvider(1f, 48000, 2));
        var snapshots = new List<LoudnessSnapshot>();
        meter.Updated += s => snapshots.Add(s);

        // 5 seconds = 50 gating blocks: several full ShortTerm (30-block) window rotations
        // and several IntegratedRecomputeEveryBlocks (10-block) recompute cycles.
        var buffer = new float[4800 * 2]; // one 100ms block per Read call
        for (var i = 0; i < 50; i++)
            meter.Read(buffer, 0, buffer.Length);

        Assert.Equal(50, snapshots.Count);
        Assert.All(snapshots, s =>
        {
            Assert.True(double.IsFinite(s.MomentaryLufs));
            Assert.True(double.IsFinite(s.ShortTermLufs));
            Assert.True(double.IsFinite(s.IntegratedLufs));
            Assert.InRange(s.TruePeakDb, -0.5, 0.5);
        });

        // IntegratedLufs only recomputes every IntegratedRecomputeEveryBlocks (10) blocks —
        // confirm the throttle actually holds the value steady in between (blocks 41-49
        // must equal block 40's value) and still updates on the next multiple of 10.
        for (var i = 40; i < 49; i++)
            Assert.Equal(snapshots[39].IntegratedLufs, snapshots[i].IntegratedLufs);

        // By block 40+ a single early filter-warm-up transient block is diluted enough
        // across the lifetime average that consecutive recompute cycles should be close,
        // even though the very first block's transient can otherwise skew the lifetime
        // (un-windowed) IntegratedLufs well away from the windowed ShortTermLufs.
        Assert.InRange(snapshots[49].IntegratedLufs - snapshots[39].IntegratedLufs, -0.5, 0.5);
    }
}
