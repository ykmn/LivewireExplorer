using System.Text;
using System.Text.RegularExpressions;
using LivewireBrowser.Core.Models;

namespace LivewireBrowser.Core.Discovery;

/// <summary>
/// Parses the Axia Livewire Advertisement protocol (UDP 4001 multicast, see
/// AdvertisementListener for port/protocol references). The byte format is not
/// publicly documented; this grammar was reverse-engineered from a real capture
/// (two packet kinds observed: an 87-byte periodic beacon with ADVT=2, and a
/// 990-byte "full info" advertisement with ADVT=1 containing per-source data).
///
/// Wire format, starting 16 bytes into the packet (the leading 16 bytes look like
/// a fixed magic/sequence header, constant "03 00 02 07" + 4 variable bytes + 8
/// zero bytes — not yet decoded, currently skipped):
///
///   record := tag(4 ASCII bytes) + type(1 byte) + value
///   value, by type byte:
///     0x00 -> 1-byte value   (group/child-count marker, e.g. NEST, INDI)
///     0x01 -> 4-byte value   (e.g. INIP = source IPv4; PSID/FSID/BSID/LPID = a
///                             4-byte id — FSID's bytes are literally the
///                             239.192.x.y / 239.193.x.y multicast address, and
///                             PSID's low 2 bytes equal the Livewire channel
///                             number, confirmed against FSID's low 2 bytes)
///     0x03 -> string: 2-byte big-endian length, then that many bytes (zero-padded ASCII)
///     0x06 -> 2-byte value
///     0x07 -> 1-byte value
///     0x08 -> 2-byte value
///     0x09 -> 8-byte value  (e.g. BUSY — a source's busy/active flag)
///
/// Per-source records are tagged dynamically "S001", "S002", ... (not a fixed
/// vocabulary entry) followed by a type 0x06 byte-length hint, then nested fields
/// including PSID (channel number), FSID (multicast address) and PSNM (channel
/// name) — PSNM is the same field name LWRP uses for a source's name. Some devices
/// send a "lightweight" source block with only PSID and BUSY (type 0x09) and no
/// FSID/PSNM at all — a source with no configured name. PSID's low 16 bits alone
/// are enough to place such a source (same channel number FSID would otherwise
/// carry, confirmed cross-checking PSID against FSID on sources that have both).
///
/// Records don't need explicit tree-structure tracking to extract what we need: a
/// flat left-to-right scan recovers every tag/value pair in order, and channel
/// boundaries are detected by the "S0xx" source tags.
/// </summary>
internal static class AdvertisementParser
{
    private static readonly Regex SourceTagPattern = new(@"^S\d{3}$", RegexOptions.Compiled);
    private const int DefaultLivewireRtpPort = 5004;
    private const int HeaderSize = 16;

    public record ParsedAdvertisement(string? DeviceName, List<ChannelInfo> Channels, string? DeviceType = null);

    public static ParsedAdvertisement Parse(byte[] packet)
    {
        var entries = ScanTlv(packet, HeaderSize);

        string? deviceName = null;
        string? deviceType = null;
        var channels = new List<ChannelInfo>();
        ChannelInfo? current = null;
        var currentOrdinal = 0;

        foreach (var (tag, value) in entries)
        {
            if (SourceTagPattern.IsMatch(tag))
            {
                currentOrdinal++;
                current = new ChannelInfo { ChannelNumber = currentOrdinal };
                channels.Add(current);
                continue;
            }

            switch (tag)
            {
                case "ATRN" when value.Length > 0:
                    deviceName = TrimNulls(value);
                    break;
                case "TYPE" when value.Length == 4:
                    deviceType = TrimNulls(value);
                    break;
                case "PSID" when current != null && value.Length == 4:
                    current.LwNumber = (value[2] << 8) | value[3];
                    // Some devices send a "lightweight" source block with only PSID
                    // and BUSY, no FSID at all — derive the multicast address from
                    // PSID's own channel number so the source isn't lost for lack of
                    // FSID. If FSID does show up (before or after PSID in the
                    // packet), it wins — don't clobber a real one.
                    if (string.IsNullOrEmpty(current.MulticastAddress))
                    {
                        current.MulticastAddress = $"239.192.{current.LwNumber >> 8}.{current.LwNumber & 0xFF}";
                        current.Port = DefaultLivewireRtpPort;
                    }
                    break;
                case "FSID" when current != null && value.Length == 4:
                    current.MulticastAddress = $"{value[0]}.{value[1]}.{value[2]}.{value[3]}";
                    current.Port = DefaultLivewireRtpPort;
                    break;
                case "PSNM" when current != null:
                    current.Name = TrimNulls(value);
                    break;
            }
        }

        // Only keep sources that actually carried a usable multicast address (from
        // PSID and/or FSID) — bare "S0xx" markers with neither mean the scan lost
        // alignment partway.
        channels.RemoveAll(c => string.IsNullOrEmpty(c.MulticastAddress));

        // A source with no PSNM (the "lightweight" BUSY-only block) has no
        // configured name — show its channel number rather than leaving it blank,
        // matching LwrpScanner.ParseSourceChannels's equivalent fallback.
        foreach (var channel in channels)
        {
            if (string.IsNullOrEmpty(channel.Name))
                channel.Name = $"Channel {channel.ChannelNumber}";
        }

        return new ParsedAdvertisement(deviceName, channels, deviceType);
    }

    private static string TrimNulls(byte[] value)
    {
        var text = Encoding.ASCII.GetString(value);
        var nullIndex = text.IndexOf('\0');
        return (nullIndex >= 0 ? text[..nullIndex] : text).Trim();
    }

    private static List<(string Tag, byte[] Value)> ScanTlv(byte[] data, int startOffset)
    {
        var result = new List<(string, byte[])>();
        var i = startOffset;

        while (i + 5 <= data.Length)
        {
            var tagBytes = data.AsSpan(i, 4);
            if (!IsPrintableTag(tagBytes))
                break;

            var tag = Encoding.ASCII.GetString(tagBytes);
            var type = data[i + 4];
            i += 5;

            int width;
            switch (type)
            {
                case 0x00:
                case 0x07:
                    width = 1;
                    break;
                case 0x06:
                case 0x08:
                    width = 2;
                    break;
                case 0x01:
                    width = 4;
                    break;
                case 0x09:
                    width = 8;
                    break;
                case 0x03:
                    if (i + 2 > data.Length)
                        return result;
                    var len = (data[i] << 8) | data[i + 1];
                    i += 2;
                    if (len < 0 || i + len > data.Length)
                        return result;
                    result.Add((tag, data[i..(i + len)]));
                    i += len;
                    continue;
                default:
                    // Unrecognised type byte — can't safely know how many bytes to
                    // skip, so stop here rather than risk misreading the rest.
                    return result;
            }

            if (i + width > data.Length)
                return result;

            result.Add((tag, data[i..(i + width)]));
            i += width;
        }

        return result;
    }

    private static bool IsPrintableTag(ReadOnlySpan<byte> bytes)
    {
        // Every observed tag is uppercase ASCII (NEST, PVER, PSNM...) or a dynamic
        // "S0xx" source index (digits), so allow both ranges.
        foreach (var b in bytes)
        {
            var isUpper = b is >= 0x41 and <= 0x5A;
            var isDigit = b is >= 0x30 and <= 0x39;
            if (!isUpper && !isDigit)
                return false;
        }

        return true;
    }
}
