using YamlDotNet.Serialization;

namespace LivewireBrowser.Core.Models;

public class ChannelInfo
{
    /// <summary>Sequential index of this source on the device itself (as reported by SRC).</summary>
    public int ChannelNumber { get; set; }

    /// <summary>
    /// The network-wide Livewire channel number, decoded from MulticastAddress
    /// (239.192.&lt;hi&gt;.&lt;lo&gt; encodes the 16-bit channel number as hi*256+lo).
    /// 0 when the address isn't in the canonical Livewire audio block or is unassigned (0.0.0.0).
    /// </summary>
    public int LwNumber { get; set; }

    /// <summary>Whether this source is actively streaming on the network (LWRP RTPE:1 vs RTPE:0).</summary>
    public bool IsActive { get; set; } = true;

    public string Name { get; set; } = string.Empty;
    public string MulticastAddress { get; set; } = string.Empty;
    public int Port { get; set; }

    // SampleRate/Channels/BitsPerSample are never reported by LWRP/SAP/Advertisement —
    // they're the decoder's fixed assumptions about the Livewire audio wire format, not
    // facts discovered about a specific device. Persisting them in devices.yaml meant a
    // stale cache entry (written by an older build) silently overrode a corrected default
    // after an app update: BitsPerSample's default moved from 16 to 24 (confirmed 24-bit
    // against a real capture), but channels loaded from an old cache file kept the
    // explicit "16" written to disk and played as noise until a full rescan. [YamlIgnore]
    // keeps these as pure code constants that always reflect the current build.
    [YamlIgnore]
    public int SampleRate { get; set; } = 48000;
    [YamlIgnore]
    public int Channels { get; set; } = 2;
    [YamlIgnore]
    public int BitsPerSample { get; set; } = 24;
}
