namespace LivewireBrowser.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>
/// Simple thread-safe file logger. Writes to &lt;app-dir&gt;/logs/app-yyyyMMdd.log,
/// rotating by day, so a tester can grab the file after a session on a remote machine
/// without hunting through %LOCALAPPDATA%.
/// </summary>
public static class Log
{
    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory = Path.Combine(AppContext.BaseDirectory, "logs");

    /// <summary>
    /// Messages below this level are dropped before they're written. Defaults to Debug
    /// (log everything) so logging behaves the same as before this setting existed until
    /// something explicitly configures it — App.xaml.cs sets it from AppSettings.LogLevel
    /// at startup, before any other component logs.
    /// </summary>
    public static LogLevel MinLevel { get; set; } = LogLevel.Debug;

    public static string LogDirectoryPath => LogDirectory;

    private static string CurrentLogFile => Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message, Exception? ex = null) =>
        Write(LogLevel.Error, ex == null ? message : $"{message}: {ex}");

    private static void Write(LogLevel level, string message)
    {
        if (level < MinLevel)
            return;

        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                File.AppendAllText(CurrentLogFile, line + Environment.NewLine);
            }
        }
        catch
        {
            // logging must never crash the app
        }
    }
}
