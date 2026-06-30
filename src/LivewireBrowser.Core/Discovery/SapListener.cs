using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using LivewireBrowser.Core.Logging;
using LivewireBrowser.Core.Models;

namespace LivewireBrowser.Core.Discovery;

/// <summary>
/// Listens for SAP/SDP announcements (RFC 2974/4566) and extracts Livewire
/// channel number, name and multicast address/port, keyed by the
/// announcing device's source IP.
/// </summary>
public class SapListener
{
    private static readonly IPAddress SapMulticastAddress = IPAddress.Parse("239.255.255.255");
    private const int SapPort = 9875;

    private static readonly Regex ChannelNumberRegex = new(@"channel[=:\s]?(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<Dictionary<string, List<ChannelInfo>>> ListenAsync(TimeSpan duration, string? localInterfaceAddress = null, CancellationToken ct = default)
    {
        var byIp = new Dictionary<string, List<ChannelInfo>>();

        try
        {
            var localAddress = string.IsNullOrWhiteSpace(localInterfaceAddress) || !IPAddress.TryParse(localInterfaceAddress, out var parsed)
                ? IPAddress.Any
                : parsed;

            using var udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, SapPort));

            if (Equals(localAddress, IPAddress.Any))
                udp.JoinMulticastGroup(SapMulticastAddress);
            else
                udp.JoinMulticastGroup(SapMulticastAddress, localAddress);

            Log.Info($"SapListener: listening on {SapMulticastAddress}:{SapPort} via interface {localAddress} for {duration}");

            using var timeoutCts = new CancellationTokenSource(duration);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                while (true)
                {
                    var result = await udp.ReceiveAsync(linkedCts.Token);
                    var sourceIp = result.RemoteEndPoint.Address.ToString();
                    var channel = TryParseSapPacket(result.Buffer);
                    if (channel == null)
                    {
                        Log.Debug($"SapListener: unparsed SAP packet from {sourceIp} ({result.Buffer.Length} bytes)");
                        continue;
                    }

                    Log.Debug($"SapListener: {sourceIp} -> channel {channel.ChannelNumber} '{channel.Name}' {channel.MulticastAddress}:{channel.Port}");

                    if (!byIp.TryGetValue(sourceIp, out var list))
                    {
                        list = new List<ChannelInfo>();
                        byIp[sourceIp] = list;
                    }

                    if (!list.Any(c => c.ChannelNumber == channel.ChannelNumber))
                        list.Add(channel);
                }
            }
            catch (OperationCanceledException)
            {
                // expected once the duration elapses
            }
        }
        catch (Exception ex)
        {
            Log.Error("SapListener: listener failed to start", ex);
        }

        Log.Info($"SapListener: finished, {byIp.Sum(kv => kv.Value.Count)} channel(s) across {byIp.Count} device(s)");
        return byIp;
    }

    internal static ChannelInfo? TryParseSapPacket(byte[] buffer)
    {
        // SAP header is 8+ bytes (version/flags, auth len, msg id hash, originating source).
        if (buffer.Length < 8)
            return null;

        var payloadOffset = 8;
        if (payloadOffset >= buffer.Length)
            return null;

        var sdpText = Encoding.UTF8.GetString(buffer, payloadOffset, buffer.Length - payloadOffset);

        string? sessionName = null;
        string? connectionAddress = null;
        int port = 0;

        foreach (var rawLine in sdpText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("s=", StringComparison.Ordinal))
                sessionName = line[2..].Trim();
            else if (line.StartsWith("c=", StringComparison.Ordinal))
            {
                var parts = line[2..].Split(' ', StringSplitOptions.TrimEntries);
                if (parts.Length >= 3)
                    connectionAddress = parts[2].Split('/')[0];
            }
            else if (line.StartsWith("m=", StringComparison.Ordinal))
            {
                var parts = line[2..].Split(' ', StringSplitOptions.TrimEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], out var parsedPort))
                    port = parsedPort;
            }
        }

        if (connectionAddress == null || port == 0)
            return null;

        var channelNumber = 0;
        if (sessionName != null)
        {
            var match = ChannelNumberRegex.Match(sessionName);
            if (match.Success)
                int.TryParse(match.Groups[1].Value, out channelNumber);
        }

        return new ChannelInfo
        {
            ChannelNumber = channelNumber,
            Name = sessionName ?? string.Empty,
            MulticastAddress = connectionAddress,
            Port = port,
        };
    }
}
