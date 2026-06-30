using LivewireBrowser.Core;
using LivewireBrowser.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LivewireBrowser.Core.Cache;

public class YamlDeviceCache
{
    private readonly string _filePath;
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public YamlDeviceCache(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(AppPaths.AppDataDirectory, "devices.yaml");

        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            // Older cache files may still have keys for properties that are now
            // [YamlIgnore] (e.g. ChannelInfo.BitsPerSample, dropped from persistence to
            // stop a stale cached value from overriding a corrected code default) — without
            // this, loading such a file throws instead of just skipping the leftover key.
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public List<DeviceInfo> Load()
    {
        if (!File.Exists(_filePath))
            return new List<DeviceInfo>();

        var yaml = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(yaml))
            return new List<DeviceInfo>();

        return _deserializer.Deserialize<List<DeviceInfo>>(yaml) ?? new List<DeviceInfo>();
    }

    public void Save(IEnumerable<DeviceInfo> devices)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var yaml = _serializer.Serialize(devices.ToList());
        File.WriteAllText(_filePath, yaml);
    }

    public void Clear()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }

    public string FilePath => _filePath;
}
