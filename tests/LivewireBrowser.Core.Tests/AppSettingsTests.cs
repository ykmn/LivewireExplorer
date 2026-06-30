using LivewireBrowser.Core.Logging;
using LivewireBrowser.Core.Settings;
using Xunit;

namespace LivewireBrowser.Core.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"lwb-settings-{Guid.NewGuid()}.yaml");

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaults()
    {
        var settings = AppSettings.Load(_tempFile);
        Assert.Equal(0, settings.AutoRescanPeriodMinutes);
        Assert.Equal(LogLevel.Warn, settings.LogLevel);
        Assert.Equal(AppLanguage.English, settings.Language);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsLanguage()
    {
        var settings = new AppSettings { Language = AppLanguage.Russian };

        settings.Save(_tempFile);
        var loaded = AppSettings.Load(_tempFile);

        Assert.Equal(AppLanguage.Russian, loaded.Language);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsLogLevel()
    {
        var settings = new AppSettings { LogLevel = LogLevel.Debug };

        settings.Save(_tempFile);
        var loaded = AppSettings.Load(_tempFile);

        Assert.Equal(LogLevel.Debug, loaded.LogLevel);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var settings = new AppSettings
        {
            AutoRescanPeriodMinutes = 30,
            DefaultOutputDeviceId = "device-123",
            LastVolume = 0.5f,
            WindowWidth = 1280,
            WindowHeight = 720,
        };

        settings.Save(_tempFile);
        var loaded = AppSettings.Load(_tempFile);

        Assert.Equal(30, loaded.AutoRescanPeriodMinutes);
        Assert.Equal("device-123", loaded.DefaultOutputDeviceId);
        Assert.Equal(0.5f, loaded.LastVolume);
        Assert.Equal(1280, loaded.WindowWidth);
        Assert.Equal(720, loaded.WindowHeight);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsWindowPosition()
    {
        var settings = new AppSettings { WindowLeft = 150.5, WindowTop = -20 };

        settings.Save(_tempFile);
        var loaded = AppSettings.Load(_tempFile);

        Assert.Equal(150.5, loaded.WindowLeft);
        Assert.Equal(-20, loaded.WindowTop);
    }

    [Fact]
    public void Load_WhenFileMissing_WindowPositionIsNull()
    {
        var settings = AppSettings.Load(_tempFile);

        Assert.Null(settings.WindowLeft);
        Assert.Null(settings.WindowTop);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }
}
