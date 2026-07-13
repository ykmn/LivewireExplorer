using LivewireBrowser.Core.Discovery;
using LivewireBrowser.Core.Models;
using Xunit;

namespace LivewireBrowser.Core.Tests;

public class DeviceClassifierTests
{
    [Theory]
    [InlineData("xNode Analog 4x4", DeviceCategory.AnalogNode)]
    [InlineData("xNode MADI", DeviceCategory.DigitalNode)]
    [InlineData("Element", DeviceCategory.Engine)]
    [InlineData("Fusion Mixing Console", DeviceCategory.Fusion)]
    [InlineData("Z/IPStream X/2", DeviceCategory.Codec)]
    [InlineData("Telos VX Prime", DeviceCategory.TelephoneHybrid)]
    [InlineData("Telos VX Engine", DeviceCategory.TelephoneHybrid)]
    [InlineData("Unknown Device 9000", DeviceCategory.Other)]
    [InlineData("Nx12 SYSV:1.2", DeviceCategory.TelephoneHybrid)]
    [InlineData("Z/IP ONE NSRC:1", DeviceCategory.Codec)]
    [InlineData("Sound4Streamer NSRC:2", DeviceCategory.Processor)]
    [InlineData("Axia xNode Analog 4x4 I/O", DeviceCategory.AnalogNode)]
    [InlineData("Axia xNode Mixed Signal I/O", DeviceCategory.DigitalNode)]
    [InlineData("Axia xNode GPIO x6", DeviceCategory.Gpio)]
    [InlineData("GPIO", DeviceCategory.Gpio)]
    [InlineData("ETH4CAN", DeviceCategory.Engine)]
    [InlineData("LiveIO", DeviceCategory.AnalogNode)]
    [InlineData("LiveAES", DeviceCategory.DigitalNode)]
    [InlineData("lwwd", DeviceCategory.IpDriver)]
    [InlineData("Axia IP-Audio Driver", DeviceCategory.IpDriver)]
    public void Classify_ReturnsExpectedCategory(string model, DeviceCategory expected)
    {
        Assert.Equal(expected, DeviceClassifier.Classify(model));
    }

    [Fact]
    public void Classify_EmptyModel_ReturnsOther()
    {
        Assert.Equal(DeviceCategory.Other, DeviceClassifier.Classify(""));
    }
}
