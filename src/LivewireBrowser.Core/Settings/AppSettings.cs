using LivewireBrowser.Core;
using LivewireBrowser.Core.Discovery;
using LivewireBrowser.Core.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LivewireBrowser.Core.Settings;

public class AppSettings
{
    public int AutoRescanPeriodMinutes { get; set; } = 0; // 0 = disabled
    public string? DefaultOutputDeviceId { get; set; }
    public float LastVolume { get; set; } = 0.8f;
    public string? LivewireNetworkInterfaceAddress { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public DiscoveryMode DiscoveryMode { get; set; } = DiscoveryMode.BruteForceAndAdvertisement;

    /// <summary>
    /// Minimum level written to logs/app-*.log. Defaults to Warn for new installs to keep
    /// the log file small during normal use — switch to Debug here when diagnosing an
    /// issue (e.g. the playback format/discovery bugs this app's history is full of).
    /// </summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Warn;

    /// <summary>UI language. Defaults to English for new installs.</summary>
    public AppLanguage Language { get; set; } = AppLanguage.English;

    private static string DefaultFilePath => Path.Combine(AppPaths.AppDataDirectory, "settings.yaml");

    public static AppSettings Load(string? filePath = null)
    {
        filePath ??= DefaultFilePath;
        if (!File.Exists(filePath))
            return new AppSettings();

        var yaml = File.ReadAllText(filePath);
        if (string.IsNullOrWhiteSpace(yaml))
            return new AppSettings();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        return deserializer.Deserialize<AppSettings>(yaml) ?? new AppSettings();
    }

    public void Save(string? filePath = null)
    {
        filePath ??= DefaultFilePath;
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        File.WriteAllText(filePath, serializer.Serialize(this));
    }
}
