using System.Net;
using System.Net.Sockets;
using System.Text;
using LivewireBrowser.Core.Logging;
using LivewireBrowser.Core.Models;

namespace LivewireBrowser.Core.Discovery;

/// <summary>
/// Passive listener for the Axia Livewire Advertisement protocol, confirmed in the
/// official Axia IP-Audio Driver manual (Appendix A, "Livewire Ports", Rev 2.10):
///
///   4000 UDP  Livewire Advertisement and Source Allocation Protocol
///             — full info advertisement requests and source allocation requests
///   4001 UDP  Livewire Advertisement and Source Allocation Protocol (mcast on 239.192.255.3)
///             — periodic announcements, full info advertisements,
///               source allocation state announcements and responses
///
/// Every Livewire device and the IP-Audio Driver periodically announce their
/// channels on this multicast group, so a passive subscriber can discover the
/// whole network's channels without sweeping every host — much faster than the
/// LWRP TCP/93 subnet sweep. The byte format (see AdvertisementParser) was
/// reverse-engineered from a real capture, the same way LwrpScanner's format was
/// confirmed against real device logs.
/// </summary>
public class AdvertisementListener
{
    private static readonly IPAddress AdvertisementMulticastAddress = IPAddress.Parse("239.192.255.3");
    private const int AdvertisementPort = 4001;

    public async Task<Dictionary<string, (string? DeviceName, List<ChannelInfo> Channels, string? DeviceType)>> ListenAsync(
        TimeSpan duration, string? localInterfaceAddress, CancellationToken ct = default)
    {
        var byIp = new Dictionary<string, (string? DeviceName, List<ChannelInfo> Channels, string? DeviceType)>();
        var packetCount = 0;

        try
        {
            var localAddress = string.IsNullOrWhiteSpace(localInterfaceAddress) || !IPAddress.TryParse(localInterfaceAddress, out var parsedAddress)
                ? IPAddress.Any
                : parsedAddress;

            using var udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, AdvertisementPort));

            if (Equals(localAddress, IPAddress.Any))
                udp.JoinMulticastGroup(AdvertisementMulticastAddress);
            else
                udp.JoinMulticastGroup(AdvertisementMulticastAddress, localAddress);

            Log.Info($"AdvertisementListener: listening on {AdvertisementMulticastAddress}:{AdvertisementPort} via interface {localAddress} for {duration}");

            using var timeoutCts = new CancellationTokenSource(duration);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                while (true)
                {
                    var result = await udp.ReceiveAsync(linkedCts.Token);
                    packetCount++;

                    var sourceIp = result.RemoteEndPoint.Address.ToString();
                    var hex = BitConverter.ToString(result.Buffer).Replace("-", " ");
                    var ascii = ToPrintableAscii(result.Buffer);
                    Log.Debug($"AdvertisementListener: {sourceIp} -> {result.Buffer.Length} bytes | hex: {hex} | ascii: {ascii}");

                    var parsed = AdvertisementParser.Parse(result.Buffer);
                    if (parsed.Channels.Count == 0 && parsed.DeviceName == null)
                        continue; // periodic beacon — no source/name info to merge

                    if (!byIp.TryGetValue(sourceIp, out var existing))
                        existing = (null, new List<ChannelInfo>(), null);

                    var deviceName = parsed.DeviceName ?? existing.DeviceName;
                    var deviceType = parsed.DeviceType ?? existing.DeviceType;
                    var channels = existing.Channels;
                    foreach (var channel in parsed.Channels)
                    {
                        if (!channels.Any(c => c.LwNumber == channel.LwNumber))
                            channels.Add(channel);
                    }

                    byIp[sourceIp] = (deviceName, channels, deviceType);

                    Log.Info($"AdvertisementListener: {sourceIp} -> name '{parsed.DeviceName}', {parsed.Channels.Count} channel(s) in this packet");
                }
            }
            catch (OperationCanceledException)
            {
                // expected once the duration elapses
            }
        }
        catch (Exception ex)
        {
            Log.Error("AdvertisementListener: listener failed to start", ex);
        }

        Log.Info($"AdvertisementListener: finished, {packetCount} packet(s) captured, {byIp.Count} device(s) with usable info");
        return byIp;
    }

    private static string ToPrintableAscii(byte[] buffer)
    {
        var sb = new StringBuilder(buffer.Length);
        foreach (var b in buffer)
            sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');

        return sb.ToString();
    }
}
