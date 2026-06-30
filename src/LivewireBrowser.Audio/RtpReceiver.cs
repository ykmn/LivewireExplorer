using System.Net;
using System.Net.Sockets;
using LivewireBrowser.Core.Logging;

namespace LivewireBrowser.Audio;

/// <summary>
/// Joins a multicast RTP stream and raises raw payload bytes (RTP header stripped)
/// for each received packet. Caller is responsible for interpreting payload format.
/// </summary>
public class RtpReceiver : IDisposable
{
    private const int FixedRtpHeaderSize = 12;

    private UdpClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;

    public event Action<byte[]>? PayloadReceived;

    /// <summary>
    /// localInterfaceAddress should be the Livewire NIC's IP (AppSettings.LivewireNetworkInterfaceAddress)
    /// on any multi-homed machine — without it, JoinMulticastGroup asks the OS to pick which NIC
    /// sends the IGMP membership report, which on a machine with more than one active adapter can
    /// silently pick the wrong one: the join call still succeeds (no exception), but the multicast
    /// traffic never arrives on that NIC, so the receive loop runs forever with 0 packets. Binding
    /// stays on IPAddress.Any (the standard pattern for multicast receivers — see MSDN), only the
    /// *group membership* needs to be pinned to a specific interface.
    /// </summary>
    public void Start(string multicastAddress, int port, string? localInterfaceAddress = null)
    {
        Stop();

        Log.Info($"RtpReceiver: joining {multicastAddress}:{port} via {localInterfaceAddress ?? "(default interface)"}");
        try
        {
            _client = new UdpClient();
            _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _client.Client.Bind(new IPEndPoint(IPAddress.Any, port));

            var multicastGroup = IPAddress.Parse(multicastAddress);
            if (!string.IsNullOrWhiteSpace(localInterfaceAddress))
                _client.JoinMulticastGroup(multicastGroup, IPAddress.Parse(localInterfaceAddress));
            else
                _client.JoinMulticastGroup(multicastGroup);
        }
        catch (Exception ex)
        {
            Log.Error($"RtpReceiver: failed to join {multicastAddress}:{port}", ex);
            throw;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_client, token), token);
    }

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken token)
    {
        var packetCount = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync(token);
                var buffer = result.Buffer;
                if (buffer.Length < FixedRtpHeaderSize)
                    continue;

                if (!TryGetPayloadRange(buffer, out var offset, out var length))
                {
                    Log.Debug($"RtpReceiver: malformed RTP packet ({buffer.Length} bytes) from {result.RemoteEndPoint}, skipped");
                    continue;
                }

                if (packetCount++ % 200 == 0)
                    Log.Debug($"RtpReceiver: received packet #{packetCount}, {buffer.Length} bytes from {result.RemoteEndPoint}");

                // First packet of every stream gets a short header summary — useful context
                // if a channel still sounds wrong, without flooding the log with a full hex
                // dump every time playback starts (that was only needed once, to confirm the
                // audio payload is 24-bit big-endian PCM — see LivewireAudioDecoder).
                if (packetCount == 1)
                {
                    var payloadType = buffer[1] & 0x7F;
                    var seq = (buffer[2] << 8) | buffer[3];
                    Log.Debug($"RtpReceiver: stream started, PT={payloadType} seq={seq} payloadOffset={offset} payloadLength={length}");
                }

                if (length <= 0)
                    continue;

                var payload = new byte[length];
                Buffer.BlockCopy(buffer, offset, payload, 0, length);
                PayloadReceived?.Invoke(payload);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on Stop()
        }
        catch (ObjectDisposedException)
        {
            // expected on Stop()
        }
        catch (Exception ex)
        {
            Log.Error("RtpReceiver: receive loop failed", ex);
        }

        Log.Info($"RtpReceiver: receive loop ended, {packetCount} packet(s) total");
    }

    /// <summary>
    /// Locates the audio payload within an RTP packet per RFC 3550. The fixed 12-byte header is
    /// only the minimum size: a variable number of CSRC identifiers (4 bytes each, count in the low
    /// nibble of byte 0) and an optional extension header (signalled by the X bit, byte 0 bit 0x10)
    /// can both push the payload further into the packet. Packets seen on real Livewire networks
    /// were previously assumed to always have a bare 12-byte header — when that assumption didn't
    /// hold, the trailing CSRC/extension bytes were fed to the decoder as if they were audio
    /// samples, which is audible as white noise. The padding bit (P, byte 0 bit 0x20) is also
    /// honored: when set, the last payload byte gives a count of padding bytes to strip from the end.
    /// </summary>
    internal static bool TryGetPayloadRange(byte[] packet, out int offset, out int length)
    {
        offset = 0;
        length = 0;

        if (packet.Length < FixedRtpHeaderSize)
            return false;

        var versionBits = (packet[0] & 0xC0) >> 6;
        if (versionBits != 2) // RTP version 2 is the only version in use
            return false;

        var hasPadding = (packet[0] & 0x20) != 0;
        var hasExtension = (packet[0] & 0x10) != 0;
        var csrcCount = packet[0] & 0x0F;

        var headerEnd = FixedRtpHeaderSize + csrcCount * 4;
        if (headerEnd > packet.Length)
            return false;

        if (hasExtension)
        {
            if (headerEnd + 4 > packet.Length)
                return false;

            var extensionWords = (packet[headerEnd + 2] << 8) | packet[headerEnd + 3];
            headerEnd += 4 + extensionWords * 4;
            if (headerEnd > packet.Length)
                return false;
        }

        var payloadLength = packet.Length - headerEnd;
        if (hasPadding && payloadLength > 0)
        {
            var paddingBytes = packet[^1];
            payloadLength -= paddingBytes;
            if (payloadLength < 0)
                return false;
        }

        offset = headerEnd;
        length = payloadLength;
        return true;
    }

    public void Stop()
    {
        if (_client != null)
            Log.Info("RtpReceiver: stopping");

        _cts?.Cancel();
        _client?.Dispose();
        _client = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Stop();
}
