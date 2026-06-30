using LivewireBrowser.Audio;
using NAudio.Wave;
using Xunit;

namespace LivewireBrowser.Core.Tests;

public class PhaseScopeMeterTests
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
    public void MonoSource_NeverRaisesSamplePairsReady()
    {
        var meter = new PhaseScopeMeter(new ConstantSampleProvider(0.5f, 48000, 1));
        var raised = false;
        meter.SamplePairsReady += _ => raised = true;

        var buffer = new float[480];
        meter.Read(buffer, 0, buffer.Length);

        Assert.False(raised);
    }

    [Fact]
    public void StereoSource_RaisesSamplePairsReadyWithCappedPointCount()
    {
        var meter = new PhaseScopeMeter(new ConstantSampleProvider(0.5f, 48000, 2));
        (float Left, float Right)[]? received = null;
        meter.SamplePairsReady += pairs => received = pairs;

        var buffer = new float[48000]; // 24000 stereo frames — far more than the 200-point cap
        meter.Read(buffer, 0, buffer.Length);

        Assert.NotNull(received);
        Assert.True(received!.Length <= 200);
        Assert.All(received, p =>
        {
            Assert.Equal(0.5f, p.Left);
            Assert.Equal(0.5f, p.Right);
        });
    }

    [Fact]
    public void StereoSource_SmallReadEmitsOnePairPerFrame()
    {
        var meter = new PhaseScopeMeter(new ConstantSampleProvider(0.25f, 48000, 2));
        (float Left, float Right)[]? received = null;
        meter.SamplePairsReady += pairs => received = pairs;

        var buffer = new float[20]; // 10 stereo frames — under the 200-point cap
        meter.Read(buffer, 0, buffer.Length);

        Assert.NotNull(received);
        Assert.Equal(10, received!.Length);
    }

    [Fact]
    public void Read_ForwardsSampleCountUnchanged()
    {
        var meter = new PhaseScopeMeter(new ConstantSampleProvider(0.1f, 48000, 2));
        var buffer = new float[100];

        var read = meter.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);
    }
}
