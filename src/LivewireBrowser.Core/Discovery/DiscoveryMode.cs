namespace LivewireBrowser.Core.Discovery;

/// <summary>How NetworkScanner looks for Livewire devices on a full scan.</summary>
public enum DiscoveryMode
{
    /// <summary>TCP/93 (LWRP) sweep of every host in the selected interface's subnet only.</summary>
    BruteForce,

    /// <summary>TCP/93 subnet sweep plus passive SAP/Advertisement multicast listening (default, current behavior).</summary>
    BruteForceAndAdvertisement,

    /// <summary>Passive SAP/Advertisement multicast listening only — no subnet sweep.</summary>
    AdvertisementOnly,
}
