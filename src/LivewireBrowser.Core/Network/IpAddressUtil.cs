namespace LivewireBrowser.Core.Network;

public static class IpAddressUtil
{
    /// <summary>
    /// Converts an IPv4 address to a numeric value suitable for sorting, so
    /// "172.22.0.9" sorts before "172.22.0.10" (unlike plain string comparison).
    /// Malformed input returns 0 rather than throwing, so a bad address just sorts
    /// first/ties with other malformed entries instead of breaking the sort.
    /// </summary>
    public static long ToSortKey(string ip)
    {
        var parts = ip.Split('.');
        if (parts.Length != 4)
            return 0;

        long key = 0;
        foreach (var part in parts)
            key = (key << 8) | (byte.TryParse(part, out var b) ? b : (byte)0);

        return key;
    }
}
