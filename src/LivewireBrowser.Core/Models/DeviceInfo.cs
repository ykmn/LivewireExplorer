namespace LivewireBrowser.Core.Models;

public class DeviceInfo
{
    public string Ip { get; set; } = string.Empty;
    public string Vendor { get; set; } = "Axia";
    public string Model { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public DeviceCategory Category { get; set; } = DeviceCategory.Other;
    public DateTime LastScanned { get; set; } = DateTime.MinValue;
    public List<ChannelInfo> Channels { get; set; } = new();
}
