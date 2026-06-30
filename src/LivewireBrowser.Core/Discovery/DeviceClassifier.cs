using LivewireBrowser.Core.Models;

namespace LivewireBrowser.Core.Discovery;

public static class DeviceClassifier
{
    private static readonly Dictionary<string, DeviceCategory> ModelMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["xNode Analog"] = DeviceCategory.AnalogNode,
        ["xNode 4x"] = DeviceCategory.AnalogNode,
        ["xNode MADI"] = DeviceCategory.DigitalNode,
        ["xNode AES"] = DeviceCategory.DigitalNode,
        ["xNode GPIO"] = DeviceCategory.Gpio,
        ["Element"] = DeviceCategory.Engine,
        ["ETH4CAN"] = DeviceCategory.Engine,
        ["iQ"] = DeviceCategory.Engine,
        ["Radius"] = DeviceCategory.Engine,
        ["Engine"] = DeviceCategory.Engine,
        ["Fusion"] = DeviceCategory.Fusion,
        ["IP-Audio Driver"] = DeviceCategory.IpDriver,
        ["Linear Acoustic"] = DeviceCategory.Codec,
        ["Z/IPStream"] = DeviceCategory.Codec,
        ["Quasar"] = DeviceCategory.Engine,
        ["VX Prime"] = DeviceCategory.TelephoneHybrid,
        ["Telos Hx"] = DeviceCategory.TelephoneHybrid,
        ["Telos VX"] = DeviceCategory.TelephoneHybrid,
        ["2x12"] = DeviceCategory.TelephoneHybrid,
        ["Nx12"] = DeviceCategory.TelephoneHybrid,
        ["Z/IP ONE"] = DeviceCategory.Codec,
        ["Sound4Streamer"] = DeviceCategory.Processor,

        // Classic (pre-xNode) Axia node firmware identifies itself only via DEVN,
        // confirmed against real 8x8 analog/AES nodes: "LiveIO" -> analog, "LiveAES"
        // already matches the bare "AES" rule below -> digital.
        ["LiveIO"] = DeviceCategory.AnalogNode,

        // DEVN:"lwwd" is not documented in the official LWRP spec, but on real
        // networks it consistently appears with NSRC/NDST/NGPI/NGPO all equal to
        // the same count (8/16/24...), matching exactly the Axia IP-Audio Driver's
        // published I/O counts (1/4/8/24-channel SKUs) — a heuristic, not a
        // protocol-guaranteed identification.
        ["lwwd"] = DeviceCategory.IpDriver,

        // Bare LWRP MODE/TYPE values seen on real xNodes when no marketing model
        // name is available (e.g. MODE:"Analog", MODE:"Mixed", MODE:"GPIO").
        ["Analog"] = DeviceCategory.AnalogNode,
        ["Digital"] = DeviceCategory.DigitalNode,
        ["AES"] = DeviceCategory.DigitalNode,
        ["Mixed"] = DeviceCategory.DigitalNode,
        ["GPIO"] = DeviceCategory.Gpio,
    };

    // Longest keys first so a specific match (e.g. "xNode Analog") wins over a bare
    // one (e.g. "Analog") regardless of dictionary enumeration order.
    private static readonly List<KeyValuePair<string, DeviceCategory>> OrderedEntries =
        ModelMap.OrderByDescending(kv => kv.Key.Length).ToList();

    public static DeviceCategory Classify(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return DeviceCategory.Other;

        foreach (var (key, category) in OrderedEntries)
        {
            if (model.Contains(key, StringComparison.OrdinalIgnoreCase))
                return category;
        }

        return DeviceCategory.Other;
    }
}
