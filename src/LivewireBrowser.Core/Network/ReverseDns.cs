using System.Net;
using LivewireBrowser.Core.Logging;

namespace LivewireBrowser.Core.Network;

/// <summary>
/// Best-effort reverse DNS (PTR) lookup. Many facilities register their Livewire
/// nodes under a meaningful hostname (e.g. "Anlg-serv-18") even when the device's
/// own LWRP self-description is generic (DEVN:"LiveIO"), so a resolvable hostname
/// is preferred as the display name when available.
/// </summary>
public static class ReverseDns
{
    public static async Task<string?> TryResolveShortNameAsync(string ip, TimeSpan timeout)
    {
        try
        {
            // Cancelling the lookup (rather than racing it with Task.WhenAny and
            // abandoning it) ensures a slow/failing DNS server can't fault a
            // detached task later on, which would surface as an unobserved task
            // exception on the finalizer thread.
            using var cts = new CancellationTokenSource(timeout);
            var entry = await Dns.GetHostEntryAsync(ip, cts.Token);

            var hostName = entry.HostName;
            if (string.IsNullOrWhiteSpace(hostName) || hostName == ip)
                return null;

            var shortName = hostName.Split('.')[0];
            return string.IsNullOrWhiteSpace(shortName) ? null : shortName;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug($"ReverseDns: lookup for {ip} failed: {ex.Message}");
            return null;
        }
    }
}
