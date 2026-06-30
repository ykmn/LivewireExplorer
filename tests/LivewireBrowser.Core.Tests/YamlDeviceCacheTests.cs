using LivewireBrowser.Core.Cache;
using LivewireBrowser.Core.Models;
using Xunit;

namespace LivewireBrowser.Core.Tests;

public class YamlDeviceCacheTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"lwb-test-{Guid.NewGuid()}.yaml");

    [Fact]
    public void Load_WhenFileMissing_ReturnsEmptyList()
    {
        var cache = new YamlDeviceCache(_tempFile);
        Assert.Empty(cache.Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsDevicesAndChannels()
    {
        var cache = new YamlDeviceCache(_tempFile);
        var devices = new List<DeviceInfo>
        {
            new()
            {
                Ip = "192.168.1.10",
                Model = "Element",
                Name = "Studio A Engine",
                Category = DeviceCategory.Engine,
                Channels = new List<ChannelInfo>
                {
                    new() { ChannelNumber = 1, Name = "Mic 1", MulticastAddress = "239.192.0.1", Port = 5004 },
                },
            },
        };

        cache.Save(devices);
        var loaded = cache.Load();

        Assert.Single(loaded);
        Assert.Equal("192.168.1.10", loaded[0].Ip);
        Assert.Equal(DeviceCategory.Engine, loaded[0].Category);
        Assert.Single(loaded[0].Channels);
        Assert.Equal(1, loaded[0].Channels[0].ChannelNumber);
        Assert.Equal("239.192.0.1", loaded[0].Channels[0].MulticastAddress);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsModelNameAndChannelFlags()
    {
        // Covers the fields added after the initial cache design: Model is now
        // distinct from Name, and channels carry LwNumber/IsActive.
        var cache = new YamlDeviceCache(_tempFile);
        var devices = new List<DeviceInfo>
        {
            new()
            {
                Ip = "172.22.0.18",
                Model = "AES/EBU 8x8 I/O",
                Name = "Anlg-serv-18",
                SerialNumber = "SN-001",
                Category = DeviceCategory.DigitalNode,
                Channels = new List<ChannelInfo>
                {
                    new()
                    {
                        ChannelNumber = 3,
                        LwNumber = 9999,
                        Name = "FMSport",
                        MulticastAddress = "239.192.39.22",
                        Port = 5004,
                        IsActive = true,
                    },
                    new()
                    {
                        ChannelNumber = 1,
                        LwNumber = 0,
                        Name = "SRC 1",
                        MulticastAddress = "239.192.0.1",
                        Port = 5004,
                        IsActive = false,
                    },
                },
            },
        };

        cache.Save(devices);
        var loaded = cache.Load();

        var device = Assert.Single(loaded);
        Assert.Equal("AES/EBU 8x8 I/O", device.Model);
        Assert.Equal("Anlg-serv-18", device.Name);
        Assert.NotEqual(device.Model, device.Name);
        Assert.Equal("SN-001", device.SerialNumber);

        var active = device.Channels.Single(c => c.ChannelNumber == 3);
        Assert.Equal(9999, active.LwNumber);
        Assert.True(active.IsActive);

        var inactive = device.Channels.Single(c => c.ChannelNumber == 1);
        Assert.Equal(0, inactive.LwNumber);
        Assert.False(inactive.IsActive);
    }

    [Fact]
    public void Load_StaleCacheWithOldBitsPerSampleKey_IgnoresItAndUsesCurrentDefault()
    {
        // Regression test: SampleRate/Channels/BitsPerSample used to be persisted, so a
        // cache file written by an older build (back when the audio format default was
        // wrongly assumed to be 16-bit) still has an explicit "bitsPerSample: 16" key on
        // disk. After making those fields [YamlIgnore], loading must neither throw nor
        // let that stale value override the current (24-bit, confirmed by a real packet
        // capture) default.
        var yaml = """
        - ip: 172.22.0.72
          model: LiveAES
          name: AES-72
          category: DigitalNode
          channels:
            - channelNumber: 1
              name: SRC 1
              multicastAddress: 239.192.43.3
              port: 5004
              sampleRate: 48000
              channels: 2
              bitsPerSample: 16
        """;
        File.WriteAllText(_tempFile, yaml);

        var cache = new YamlDeviceCache(_tempFile);
        var loaded = cache.Load();

        var device = Assert.Single(loaded);
        var channel = Assert.Single(device.Channels);
        Assert.Equal(24, channel.BitsPerSample);
    }

    [Fact]
    public void Clear_RemovesFile()
    {
        var cache = new YamlDeviceCache(_tempFile);
        cache.Save(new List<DeviceInfo> { new() { Ip = "10.0.0.1" } });
        Assert.True(File.Exists(_tempFile));

        cache.Clear();
        Assert.False(File.Exists(_tempFile));
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }
}
