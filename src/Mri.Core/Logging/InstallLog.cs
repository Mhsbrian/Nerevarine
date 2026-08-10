using System.Globalization;
using System.Runtime.InteropServices;

namespace Mri.Core.Logging;

/// <summary>
/// Per-run diagnostic log. One file per install attempt in &lt;install&gt;/logs/,
/// designed so that the file alone is enough to debug a failure from another
/// machine. Registered secrets (the Nexus API key) are redacted from every
/// line, writes are thread-safe and auto-flushed (crash = log still on disk),
/// and logging failures never break the install.
/// </summary>
public sealed class InstallLog : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly List<string> _redactions = [];
    private readonly object _lock = new();
    private bool _disposed;

    public string FilePath { get; }

    private InstallLog(string filePath, StreamWriter writer)
    {
        FilePath = filePath;
        _writer = writer;
    }

    public static InstallLog CreateInDirectory(string logsDir, string appVersion)
    {
        Directory.CreateDirectory(logsDir);
        var path = Path.Combine(logsDir, $"installer-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var writer = new StreamWriter(
            File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
        };

        var log = new InstallLog(path, writer);
        log.Info("app", $"Morrowind Remake Installer v{appVersion} — run started {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        log.Info("app", $"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        log.Info("app", $"runtime: {RuntimeInformation.FrameworkDescription}");
        log.Info("app", $"culture: {CultureInfo.CurrentCulture.Name}, timezone: {TimeZoneInfo.Local.Id}");
        return log;
    }

    /// <summary>Registers a secret that must never appear in the log or diagnostics.</summary>
    public void AddRedaction(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 4)
            return;
        lock (_lock)
            _redactions.Add(secret);
    }

    public void Info(string category, string message) => Write("INFO ", category, message);

    public void Warn(string category, string message) => Write("WARN ", category, message);

    public void Error(string category, string message, Exception? exception = null)
    {
        Write("ERROR", category, message);
        if (exception is not null)
            Write("ERROR", category, exception.ToString());
    }

    public string Redact(string text)
    {
        lock (_lock)
        {
            foreach (var secret in _redactions)
                text = text.Replace(secret, "«redacted»");
        }
        return text;
    }

    private void Write(string level, string category, string message)
    {
        try
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
                foreach (var secret in _redactions)
                    message = message.Replace(secret, "«redacted»");
                foreach (var line in message.Split('\n'))
                    _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{level}] [{category}] {line.TrimEnd('\r')}");
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // A full disk or closed stream must never take the install down.
        }
    }

    public void Dispose()
    {
        try
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
                _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [INFO ] [app] — end of log —");
                _disposed = true;
                _writer.Dispose();
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
        }
    }
}
