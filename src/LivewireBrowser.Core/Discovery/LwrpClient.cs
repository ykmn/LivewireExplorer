using System.Net.Sockets;
using System.Text;
using LivewireBrowser.Core.Logging;

namespace LivewireBrowser.Core.Discovery;

/// <summary>
/// Minimal client for the Livewire Routing Protocol (LWRP) — a line-based Telnet-style
/// control protocol that every Axia/Telos Livewire device runs on TCP port 93.
/// Used here purely for read-only queries (VER, SRC) to discover devices and their
/// channel list; no LOGIN/state-changing commands are issued.
/// </summary>
public class LwrpClient : IDisposable
{
    public const int Port = 93;

    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public async Task<bool> ConnectAsync(string ip, TimeSpan timeout, CancellationToken ct = default)
    {
        try
        {
            _client = new TcpClient();
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            await _client.ConnectAsync(ip, Port, linkedCts.Token);

            var stream = _client.GetStream();
            _reader = new StreamReader(stream, Encoding.ASCII);
            _writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };
            return true;
        }
        catch
        {
            Dispose();
            return false;
        }
    }

    /// <summary>
    /// Sends a command and collects every line the device sends back within the given
    /// window. LWRP has no universal end-of-response marker for plain queries, so we
    /// simply gather whatever arrives until the timeout elapses.
    /// </summary>
    public async Task<List<string>> QueryAsync(string command, TimeSpan timeout, CancellationToken ct = default)
    {
        var lines = new List<string>();
        if (_writer == null || _reader == null)
            return lines;

        try
        {
            await _writer.WriteLineAsync(command);

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            while (true)
            {
                var line = await _reader.ReadLineAsync(linkedCts.Token);
                if (line == null)
                    break;
                lines.Add(line);
            }
        }
        catch (OperationCanceledException)
        {
            // expected once the collection window elapses
        }
        catch (IOException ex)
        {
            Log.Debug($"LwrpClient: connection closed while querying '{command}': {ex.Message}");
        }

        return lines;
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _writer?.Dispose();
        _client?.Dispose();
        _reader = null;
        _writer = null;
        _client = null;
    }
}
