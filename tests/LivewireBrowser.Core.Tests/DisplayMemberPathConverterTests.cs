using LivewireBrowser.App;
using Xunit;

namespace LivewireBrowser.Core.Tests;

public class DisplayMemberPathConverterTests
{
    private record Sample(string Mode, string Label);

    [Fact]
    public void Convert_WithDisplayMemberPath_ReturnsThatPropertyNotToString()
    {
        var converter = new DisplayMemberPathConverter();
        var item = new Sample("ByIp", "По IP-адресу");

        var result = converter.Convert(new object[] { item, "Label" }, typeof(string), null!, null!);

        Assert.Equal("По IP-адресу", result);
    }

    [Fact]
    public void Convert_WithoutDisplayMemberPath_FallsBackToToString()
    {
        var converter = new DisplayMemberPathConverter();

        var result = converter.Convert(new object[] { Core.Logging.LogLevel.Warn, null! }, typeof(string), null!, null!);

        Assert.Equal("Warn", result);
    }

    [Fact]
    public void Convert_NullSelectedItem_ReturnsEmptyString()
    {
        var converter = new DisplayMemberPathConverter();

        var result = converter.Convert(new object[] { null!, "Label" }, typeof(string), null!, null!);

        Assert.Equal(string.Empty, result);
    }
}
