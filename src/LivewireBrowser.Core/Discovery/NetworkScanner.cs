using LivewireBrowser.Core.Logging;
using LivewireBrowser.Core.Models;

namespace LivewireBrowser.Core.Discovery;

public class NetworkScanner
{
    private readonly LwrpScanner _lwrpScanner;
    private readonly SapListener _sapListener;
    private readonly AdvertisementListener _advertisementListener;

    /// <summary>
    /// IP address of the local network interface connected to the Livewire network.
    /// Required for both the TCP/93 subnet sweep and SAP multicast join — without it,
    /// traffic goes out the OS default route, which on multi-homed machines is rarely
    /// the Livewire NIC.
    /// </summary>
    public string? NetworkInterfaceAddress { get; set; }

    public NetworkScanner(LwrpScanner? lwrpScanner = null, SapListener? sapListener = null, AdvertisementListener? advertisementListener = null)
    {
        _lwrpScanner = lwrpScanner ?? new LwrpScanner();
        _sapListener = sapListener ?? new SapListener();
        _advertisementListener = advertisementListener ?? new AdvertisementListener();
    }

    public async Task<List<DeviceInfo>> FullScanAsync(TimeSpan timeout, IProgress<(int Completed, int Total)>? progress = null,
        IProgress<string>? status = null, CancellationToken ct = default, DiscoveryMode mode = DiscoveryMode.BruteForceAndAdvertisement)
    {
        Log.Info($"NetworkScanner: full scan started, timeout {timeout}, interface {NetworkInterfaceAddress ?? "(none selected)"}, mode {mode}");

        var bruteForce = mode is DiscoveryMode.BruteForce or DiscoveryMode.BruteForceAndAdvertisement;
        var advertisement = mode is DiscoveryMode.BruteForceAndAdvertisement or DiscoveryMode.AdvertisementOnly;

        // Real captures show each device's "full info" advertisement (the only kind that
        // carries a name and per-channel multicast addresses) arriving roughly once every
        // tens of seconds — most packets in between are short identity beacons or source
        // allocation state packets with no name/address. When advertisements are the *only*
        // discovery source, the short `timeout` used to merely supplement the TCP/93 sweep
        // misses most devices, even though they're on the network. Give passive listening
        // enough time to catch each device's turn instead.
        var advertisementListenDuration = mode == DiscoveryMode.AdvertisementOnly
            ? TimeSpan.FromSeconds(Math.Max(timeout.TotalSeconds, 45))
            : timeout;

        if (mode == DiscoveryMode.AdvertisementOnly)
            status?.Report($"Advertisement scan: passive listening for {advertisementListenDuration.TotalSeconds:0}s...");

        var lwrpTask = bruteForce
            ? _lwrpScanner.ScanSubnetAsync(NetworkInterfaceAddress, TimeSpan.FromMilliseconds(300), progress, status, ct)
            : Task.FromResult(new List<DeviceInfo>());
        var sapTask = advertisement
            ? _sapListener.ListenAsync(advertisementListenDuration, NetworkInterfaceAddress, ct)
            : Task.FromResult(new Dictionary<string, List<ChannelInfo>>());
        var advertisementTask = advertisement
            ? _advertisementListener.ListenAsync(advertisementListenDuration, NetworkInterfaceAddress, ct)
            : Task.FromResult(new Dictionary<string, (string? DeviceName, List<ChannelInfo> Channels)>());

        await Task.WhenAll(lwrpTask, sapTask, advertisementTask);

        var devices = await lwrpTask;
        var channelsByIp = await sapTask;
        var advertisementByIp = await advertisementTask;

        foreach (var device in devices)
        {
            if (!channelsByIp.TryGetValue(device.Ip, out var sapChannels))
                continue;

            foreach (var channel in sapChannels)
            {
                if (!device.Channels.Any(c => c.ChannelNumber == channel.ChannelNumber))
                    device.Channels.Add(channel);
            }

            device.Channels = device.Channels.OrderBy(c => c.ChannelNumber).ToList();
        }

        foreach (var device in devices)
        {
            if (!advertisementByIp.TryGetValue(device.Ip, out var advertised))
                continue;

            foreach (var channel in advertised.Channels)
            {
                if (!device.Channels.Any(c => c.LwNumber != 0 && c.LwNumber == channel.LwNumber))
                    device.Channels.Add(channel);
            }

            device.Channels = device.Channels.OrderBy(c => c.ChannelNumber).ToList();

            // Advertisement's ATRN name is the device's own configured name, same tier
            // as LWRP's "IP" hostname — prefer it over a generic DEVN/model fallback.
            if (!string.IsNullOrWhiteSpace(advertised.DeviceName) && device.Model == "LWRP device")
                device.Name = advertised.DeviceName!;
        }

        // Advertisement is multicast-based and may see devices the TCP/93 subnet sweep
        // missed entirely (wrong subnet range, host briefly unreachable, etc.) — list
        // them too rather than silently dropping channels we already received.
        foreach (var (ip, advertised) in advertisementByIp)
        {
            if (devices.Any(d => d.Ip == ip) || advertised.Channels.Count == 0 && string.IsNullOrWhiteSpace(advertised.DeviceName))
                continue;

            var name = advertised.DeviceName ?? $"Livewire device ({ip})";
            devices.Add(new DeviceInfo
            {
                Ip = ip,
                Model = "Livewire Advertisement",
                Name = name,
                Category = DeviceClassifier.Classify(name),
                LastScanned = DateTime.UtcNow,
                Channels = advertised.Channels.OrderBy(c => c.ChannelNumber).ToList(),
            });
        }

        Log.Info($"NetworkScanner: full scan finished, {devices.Count} device(s) found");
        return devices;
    }

    public async Task<DeviceInfo?> RescanDeviceAsync(DeviceInfo existing, TimeSpan timeout, IProgress<string>? status = null, CancellationToken ct = default)
    {
        Log.Info($"NetworkScanner: rescanning device {existing.Ip}");

        var updated = await _lwrpScanner.ProbeHostAsync(existing.Ip, timeout, status, ct) ?? existing;
        updated.Ip = existing.Ip;
        updated.LastScanned = DateTime.UtcNow;

        Log.Info($"NetworkScanner: rescan of {existing.Ip} finished, {updated.Channels.Count} channel(s)");
        return updated;
    }
}
