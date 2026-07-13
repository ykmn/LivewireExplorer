using System.Threading;
using LivewireBrowser.Core.Logging;
using LivewireBrowser.Core.Models;
using LivewireBrowser.Core.Network;

namespace LivewireBrowser.Core.Discovery;

/// <summary>
/// Discovers Livewire devices by sweeping the selected interface's subnet on TCP/93
/// (LWRP) and querying VER (device info) and SRC (this node's source channels).
/// Attribute names below (DEVN, PRODUCT, MODEL, PSNM, RTPA) were confirmed against
/// real device responses (see logs) rather than guessed; exact field availability
/// still varies across firmware generations (older nodes report DEVN only, newer
/// xNodes also report PRODUCT/MODEL), so the richer fields are preferred when present.
/// </summary>
public class LwrpScanner
{
    private const int DefaultLivewireRtpPort = 5004;
    private const int MaxConcurrentProbes = 64;

    public async Task<List<DeviceInfo>> ScanSubnetAsync(string? localInterfaceAddress, TimeSpan connectTimeout, TimeSpan queryTimeout,
        IProgress<(int Completed, int Total)>? progress = null, IProgress<string>? status = null, CancellationToken ct = default)
    {
        var hosts = NetworkInterfaceHelper.GetHostAddresses(localInterfaceAddress);
        if (hosts.Count == 0)
        {
            Log.Warn("LwrpScanner: no Livewire network interface selected (or it has no IPv4 address) — select one in Settings");
            progress?.Report((0, 0));
            return new List<DeviceInfo>();
        }

        Log.Info($"LwrpScanner: sweeping {hosts.Count} host(s) on TCP/{LwrpClient.Port} via {localInterfaceAddress}");

        using var semaphore = new SemaphoreSlim(MaxConcurrentProbes);
        var results = new List<DeviceInfo>();
        var resultsLock = new object();
        var completed = 0;

        progress?.Report((0, hosts.Count));

        var tasks = hosts.Select(async ip =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var device = await ProbeHostAsync(ip, connectTimeout, queryTimeout, status, ct);
                if (device != null)
                {
                    lock (resultsLock)
                        results.Add(device);
                }
            }
            finally
            {
                semaphore.Release();
                var done = Interlocked.Increment(ref completed);
                progress?.Report((done, hosts.Count));
            }
        });

        await Task.WhenAll(tasks);

        Log.Info($"LwrpScanner: sweep finished, {results.Count} device(s) responded on TCP/{LwrpClient.Port}");
        status?.Report($"Scan complete: {results.Count} device(s) found");
        return results;
    }

    public async Task<DeviceInfo?> ProbeHostAsync(string ip, TimeSpan connectTimeout, TimeSpan queryTimeout, IProgress<string>? status = null, CancellationToken ct = default)
    {
        status?.Report($"{ip}: checking TCP/93...");

        using var client = new LwrpClient();
        try
        {
            if (!await client.ConnectAsync(ip, connectTimeout, ct))
                return null;

            // Only worth resolving once something is actually listening on TCP/93 —
            // starting this for every swept address (the vast majority of which have
            // no device at all on a typical /16 sweep) floods the OS resolver with
            // thousands of concurrent lookups that outlive this method's own
            // MaxConcurrentProbes gate (ProbeHostAsync returns as soon as ConnectAsync
            // fails, but a detached dnsTask keeps running) — in practice this serializes
            // on the resolver and can keep a subnet sweep "running" for minutes after
            // every real device has already answered, while also starving the thread
            // pool for unrelated concurrent work (SAP/Advertisement listening, UI
            // updates). Independent of the LWRP session, so still kick it off now and
            // only wait on it once we already have a device to name.
            var dnsTask = ReverseDns.TryResolveShortNameAsync(ip, TimeSpan.FromMilliseconds(800));

            Log.Debug($"LwrpScanner: {ip} responded on TCP/{LwrpClient.Port}, querying VER/SRC");
            status?.Report($"{ip}: device found, querying VER...");

            var verLines = await client.QueryAsync("VER", queryTimeout, ct);

            status?.Report($"{ip}: querying SRC (channel list)...");
            var srcLines = await client.QueryAsync("SRC", queryTimeout, ct);
            if (srcLines.Count == 0)
            {
                Log.Debug($"LwrpScanner: {ip} gave no response to 'SRC', trying 'SRC ALL'");
                srcLines = await client.QueryAsync("SRC ALL", queryTimeout, ct);
            }

            status?.Report($"{ip}: querying IP (configured device name)...");
            var ipLines = await client.QueryAsync("IP", queryTimeout, ct);

            foreach (var line in verLines)
                Log.Debug($"LwrpScanner: {ip} VER> {line}");
            foreach (var line in srcLines)
                Log.Debug($"LwrpScanner: {ip} SRC> {line}");
            foreach (var line in ipLines)
                Log.Debug($"LwrpScanner: {ip} IP> {line}");

            var device = BuildDeviceInfo(ip, verLines);
            device.Channels = ParseSourceChannels(srcLines);

            // Naming priority: (1) the device's own configured hostname, reported by
            // the official LWRP "IP" query (e.g. "Anlg-serv-18") — the most authoritative
            // source, since it's set on the unit itself rather than guessed externally;
            // (2) for IP-Audio Driver "devices" (PC software, not hardware), the actual
            // Windows computer name via NetBIOS Name Service — these PCs are often
            // missing a reverse-DNS record but reliably answer NBNS; (3) a facility's
            // reverse-DNS record; (4) DEVN/PRODUCT+MODEL self-description as a last resort.
            var configuredHostname = ExtractIpHostname(ipLines);
            if (configuredHostname != null)
            {
                Log.Debug($"LwrpScanner: {ip} reports configured hostname '{configuredHostname}' via IP query");
                device.Name = configuredHostname;
            }
            else
            {
                string? hostname = null;

                if (device.Category == DeviceCategory.IpDriver)
                {
                    hostname = await NetBiosNameResolver.TryResolveAsync(ip, TimeSpan.FromSeconds(1), ct);
                    if (hostname != null)
                        Log.Debug($"LwrpScanner: {ip} resolved NetBIOS computer name '{hostname}' (IP-Audio Driver host)");
                }

                hostname ??= await dnsTask;
                if (hostname != null)
                {
                    Log.Debug($"LwrpScanner: {ip} resolved hostname '{hostname}'");
                    device.Name = hostname;
                }
                else
                {
                    // Nothing resolved, so the name is just the generic DEVN/model
                    // self-description (e.g. "LiveIO") — identical across every node of
                    // that type. Suffix the IP so the device list doesn't show
                    // indistinguishable duplicate entries.
                    Log.Debug($"LwrpScanner: {ip} has no resolvable hostname, disambiguating generic name '{device.Name}' with IP");
                    device.Name = $"{device.Name} ({ip})";
                }
            }

            Log.Info($"LwrpScanner: {ip} -> model '{device.Model}', name '{device.Name}', {device.Channels.Count} channel(s)");
            status?.Report($"{ip}: found {device.Name}, {device.Channels.Count} channel(s)");
            return device;
        }
        finally
        {
            // LwrpClient is disposed by the `using` above (closes the TCP connection);
            // logged explicitly so a connection leak would be visible in the log.
            Log.Debug($"LwrpScanner: closed connection to {ip}:{LwrpClient.Port}");
        }
    }

    /// <summary>
    /// Per the official LWRP spec, sending "IP" with no parameters returns the
    /// device's current network configuration, including the unit's own configured
    /// hostname (DNS-compliant, max 12 chars) — not a guess. Confirmed against real
    /// devices to come back as two separate lines, in two different formats:
    /// `IP ADDR:"..." LINK:1` (colon key:value, like VER/SRC) and, separately,
    /// `IP hostname air-dsp2` (bare space-separated word pair, no colon at all).
    /// </summary>
    internal static string? ExtractIpHostname(List<string> ipLines)
    {
        foreach (var line in ipLines)
        {
            var tokens = Tokenize(line);

            // Don't assume the line starts with a literal "IP" echo to skip — the spec
            // doesn't actually document the response's leading token, only the query
            // syntax. ParseAttributes already discards a bare leading word with no
            // colon on its own, so passing every token is strictly safer.
            var attributes = ParseAttributes(tokens);
            var hostname = attributes.GetValueOrDefault("hostname");
            if (!string.IsNullOrWhiteSpace(hostname))
                return hostname;

            // Bare "hostname <value>" form (no colon) seen on real firmware.
            for (var i = 0; i < tokens.Count - 1; i++)
            {
                if (string.Equals(tokens[i], "hostname", StringComparison.OrdinalIgnoreCase))
                    return tokens[i + 1];
            }
        }

        return null;
    }

    private static readonly Dictionary<string, string> DevnDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GPIO"] = "Axia Node GPIO",
        ["LiveRT"] = "Axia Router Selector",
        ["LiveAES"] = "Axia Node AES x8",
        ["LiveIO"] = "Axia Node Analog 8x8",
        ["Element"] = "Element PSU",
        ["Engine"] = "Studio Engine",
        ["Fusion"] = "Studio Fusion",
    };

    private static DeviceInfo BuildDeviceInfo(string ip, List<string> verLines)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in verLines)
        {
            // See ExtractIpHostname: don't assume a leading "VER" echo to skip.
            foreach (var (key, value) in ParseAttributes(Tokenize(line)))
                attributes[key] = value;
        }

        // Newer Axia xNodes report PRODUCT ("Axia xNode") + MODEL ("Analog 4x4 I/O"),
        // which makes the best display name. Older/other firmware (classic xNodes,
        // Engine, Fusion, Z/IP ONE, Sound4Streamer, Nx12...) only report DEVN, which
        // is then itself the most useful identifier available.
        var product = attributes.GetValueOrDefault("PRODUCT");
        var modelAttr = attributes.GetValueOrDefault("MODEL");
        var devn = attributes.GetValueOrDefault("DEVN");

        var model = (product, modelAttr) switch
        {
            (not null, not null) => $"{product} {modelAttr}",
            (null, not null) => modelAttr,
            (not null, null) => product,
            // DEVN:"lwwd" isn't documented by the official spec, but on real networks
            // it consistently identifies the Axia IP-Audio Driver (Windows software,
            // not a hardware node) — see DeviceClassifier for the matching heuristic.
            _ when string.Equals(devn, "lwwd", StringComparison.OrdinalIgnoreCase) => "Axia IP-Audio Driver",
            // Classic (pre-xNode) Axia hardware only reports a bare DEVN, which is a
            // legacy internal codename — translate it to the unit's actual marketing
            // name for display.
            _ when devn is not null && DevnDisplayNames.TryGetValue(devn, out var friendly) => friendly,
            _ => devn ?? "LWRP device",
        };

        // For "lwwd" the raw DEVN is meaningless to a human, so default to the nicer
        // model name too; ProbeHostAsync overrides this with the unit's configured
        // hostname (or reverse DNS) when available, which is preferred either way.
        var name = string.Equals(devn, "lwwd", StringComparison.OrdinalIgnoreCase) ? model : devn ?? model;

        var classifierText = string.Join(" ", attributes.Values);

        return new DeviceInfo
        {
            Ip = ip,
            Model = model,
            Name = name,
            Category = DeviceClassifier.Classify(classifierText.Length > 0 ? classifierText : model),
            LastScanned = DateTime.UtcNow,
        };
    }

    internal static List<ChannelInfo> ParseSourceChannels(List<string> lines)
    {
        var channels = new List<ChannelInfo>();

        foreach (var line in lines)
        {
            if (!line.StartsWith("SRC", StringComparison.OrdinalIgnoreCase))
                continue;

            var tokens = Tokenize(line);
            if (tokens.Count < 2 || !int.TryParse(tokens[1], out var channelNumber))
                continue;

            var attributes = ParseAttributes(tokens.Skip(2));

            var address = attributes.GetValueOrDefault("RTPA") ?? attributes.GetValueOrDefault("ADDR");
            if (string.IsNullOrEmpty(address))
                continue;

            var name = attributes.GetValueOrDefault("PSNM") ?? attributes.GetValueOrDefault("NAME") ?? $"Channel {channelNumber}";
            var (multicastAddress, port) = SplitAddressPort(address);

            // RTPE ("RTP Enable") marks whether the source is actually streaming on the
            // network; RTPE:0 sources still report a (often placeholder) address but are
            // not live — default to active when the field is absent (older firmware).
            var isActive = attributes.GetValueOrDefault("RTPE") != "0";

            if (channels.Any(c => c.ChannelNumber == channelNumber))
                continue;

            channels.Add(new ChannelInfo
            {
                ChannelNumber = channelNumber,
                LwNumber = ComputeLwNumber(multicastAddress),
                Name = name,
                MulticastAddress = multicastAddress,
                Port = port,
                IsActive = isActive,
            });
        }

        return channels.OrderBy(c => c.ChannelNumber).ToList();
    }

    /// <summary>
    /// Livewire encodes its 16-bit channel number directly in the 239.192.0.0/16
    /// multicast block: address 239.192.&lt;hi&gt;.&lt;lo&gt; <=> channel hi*256+lo.
    /// Returns 0 for addresses outside that block or the unassigned 0.0.0.0.
    /// </summary>
    internal static int ComputeLwNumber(string multicastAddress)
    {
        var parts = multicastAddress.Split('.');
        if (parts.Length != 4)
            return 0;
        if (!byte.TryParse(parts[0], out var a) || !byte.TryParse(parts[1], out var b))
            return 0;
        if (a != 239 || b != 192)
            return 0;
        if (!byte.TryParse(parts[2], out var hi) || !byte.TryParse(parts[3], out var lo))
            return 0;

        return hi * 256 + lo;
    }

    /// <summary>
    /// Splits a line on whitespace, treating "double quoted phrases" (which LWRP uses
    /// for names containing spaces, e.g. NAME:"Studio A Mic") as a single token with
    /// the quotes stripped.
    /// </summary>
    internal static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    /// <summary>
    /// A token like "PSNM:Studio Mic" (already de-quoted by Tokenize) becomes one
    /// attribute. A bare token with no colon at all is treated as a continuation of
    /// the previous attribute's value, covering any unquoted multi-word values that
    /// might still occur. Per the official LWRP spec, attribute names aren't always
    /// uppercase (VER uses short ALLCAPS codes like DEVN, but IP uses descriptive
    /// lowercase names like "hostname"), so any token containing ':' is a new key
    /// regardless of case.
    /// </summary>
    private static Dictionary<string, string> ParseAttributes(IEnumerable<string> tokens)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;

        foreach (var token in tokens)
        {
            var idx = token.IndexOf(':');
            var looksLikeNewKey = idx > 0;

            if (looksLikeNewKey)
            {
                currentKey = token[..idx];
                result[currentKey] = token[(idx + 1)..];
            }
            else if (currentKey != null)
            {
                result[currentKey] += " " + token;
            }
        }

        return result;
    }

    private static (string Address, int Port) SplitAddressPort(string value)
    {
        var idx = value.IndexOf(':');
        if (idx > 0 && int.TryParse(value[(idx + 1)..], out var parsedPort))
            return (value[..idx], parsedPort);

        return (value, DefaultLivewireRtpPort);
    }
}
