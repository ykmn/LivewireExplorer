using System.Net;
using System.Net.Sockets;
using System.Text;
using LivewireBrowser.Core.Logging;

namespace LivewireBrowser.Core.Network;

/// <summary>
/// Resolves a Windows computer's actual NetBIOS name via the NetBIOS Name Service
/// (RFC 1002, UDP/137 "Node Status" query) — the same mechanism behind `nbtstat -A`.
/// Used as a fallback for devices that are PC software (e.g. the Axia IP-Audio
/// Driver) rather than a Livewire hardware node: such PCs may have no reverse-DNS
/// record, but they reliably answer NBNS with their real computer name.
/// </summary>
public static class NetBiosNameResolver
{
    private const int NbnsPort = 137;
    private const byte WorkstationServiceSuffix = 0x00;

    // Well-known encoded "*" wildcard name used by NBSTAT queries to ask a host to
    // list every NetBIOS name it has registered, regardless of what it's actually named.
    private static readonly byte[] EncodedWildcardName = Encoding.ASCII.GetBytes("CKAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

    public static async Task<string?> TryResolveAsync(string ip, TimeSpan timeout, CancellationToken ct = default)
    {
        try
        {
            using var udp = new UdpClient();
            var query = BuildNodeStatusQuery();
            await udp.SendAsync(query, query.Length, new IPEndPoint(IPAddress.Parse(ip), NbnsPort));

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var result = await udp.ReceiveAsync(linkedCts.Token);
            return ParseNodeStatusResponse(result.Buffer);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug($"NetBiosNameResolver: lookup for {ip} failed: {ex.Message}");
            return null;
        }
    }

    private static byte[] BuildNodeStatusQuery()
    {
        var buffer = new byte[12 + 1 + 32 + 1 + 2 + 2];

        // Header: arbitrary transaction id, standard query flags, 1 question.
        buffer[0] = 0x12;
        buffer[1] = 0x34;
        buffer[5] = 0x01;

        var offset = 12;
        buffer[offset++] = 0x20; // encoded name length (32 bytes)
        Array.Copy(EncodedWildcardName, 0, buffer, offset, EncodedWildcardName.Length);
        offset += EncodedWildcardName.Length;
        buffer[offset++] = 0x00; // name terminator

        buffer[offset++] = 0x00; buffer[offset++] = 0x21; // QTYPE: NBSTAT
        buffer[offset++] = 0x00; buffer[offset++] = 0x01; // QCLASS: IN

        return buffer;
    }

    /// <summary>
    /// NODE STATUS RESPONSE = header(12) + RR name(1+32+1) + type/class/ttl/rdlength(10)
    /// + NUM_NAMES(1) + NUM_NAMES * [name(15) + suffix(1) + flags(2)]. The Workstation
    /// Service entry (suffix 0x00) holds the machine's actual computer name.
    /// </summary>
    internal static string? ParseNodeStatusResponse(byte[] data)
    {
        var offset = 12 + 1 + 32 + 1 + 2 + 2 + 4 + 2;
        if (offset >= data.Length)
            return null;

        var numNames = data[offset];
        offset += 1;

        for (var i = 0; i < numNames; i++)
        {
            if (offset + 18 > data.Length)
                break;

            var name = Encoding.ASCII.GetString(data, offset, 15).TrimEnd(' ', '\0');
            var suffix = data[offset + 15];
            offset += 18;

            if (suffix == WorkstationServiceSuffix && !string.IsNullOrWhiteSpace(name))
                return name;
        }

        return null;
    }
}
