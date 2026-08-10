using Mri.Core.Logging;

namespace Mri.Core.Tests.Logging;

public class InstallLogTests : IDisposable
{
    private readonly string _dir;

    public InstallLogTests() =>
        _dir = Directory.CreateTempSubdirectory("mri-log-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void WritesHeaderEntriesAndFooter()
    {
        string path;
        using (var log = InstallLog.CreateInDirectory(_dir, "1.2.3"))
        {
            path = log.FilePath;
            log.Info("engine", "step one starting");
            log.Warn("proc", "something odd");
            log.Error("engine", "boom", new InvalidOperationException("kaput"));
        }

        var text = File.ReadAllText(path);
        Assert.Contains("Morrowind Remake Installer v1.2.3", text);
        Assert.Contains("OS:", text);
        Assert.Contains("[INFO ] [engine] step one starting", text);
        Assert.Contains("[WARN ] [proc] something odd", text);
        Assert.Contains("[ERROR] [engine] boom", text);
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("kaput", text);
        Assert.Contains("— end of log —", text);
    }

    [Fact]
    public void RedactsSecretsEverywhere()
    {
        string path;
        using (var log = InstallLog.CreateInDirectory(_dir, "1.0"))
        {
            path = log.FilePath;
            log.AddRedaction("super-secret-api-key-123");
            log.Info("proc", "spawn: umo.exe with key super-secret-api-key-123 inline");
            log.Error("app", "failed", new Exception("key super-secret-api-key-123 leaked in message"));
        }

        var text = File.ReadAllText(path);
        Assert.DoesNotContain("super-secret-api-key-123", text);
        Assert.Contains("«redacted»", text);
    }

    [Fact]
    public void ShortOrEmptySecretsAreIgnored()
    {
        using var log = InstallLog.CreateInDirectory(_dir, "1.0");
        log.AddRedaction("");   // must not turn every empty match into noise
        log.AddRedaction("ab"); // too short to be a real secret
        log.Info("app", "abABab");
        // No assertion failure = pass; content check:
        log.Dispose();
        Assert.Contains("abABab", File.ReadAllText(log.FilePath));
    }

    [Fact]
    public void MultilineMessagesGetPerLinePrefix()
    {
        string path;
        using (var log = InstallLog.CreateInDirectory(_dir, "1.0"))
        {
            path = log.FilePath;
            log.Info("proc", "line one\nline two");
        }

        var lines = File.ReadAllLines(path);
        Assert.Contains(lines, l => l.Contains("[proc] line one"));
        Assert.Contains(lines, l => l.Contains("[proc] line two"));
    }

    [Fact]
    public async Task ConcurrentWritesDoNotInterleave()
    {
        string path;
        using (var log = InstallLog.CreateInDirectory(_dir, "1.0"))
        {
            path = log.FilePath;
            await Task.WhenAll(Enumerable.Range(0, 8).Select(n => Task.Run(() =>
            {
                for (var i = 0; i < 50; i++)
                    log.Info($"t{n}", $"message-{n}-{i}");
            })));
        }

        var lines = File.ReadAllLines(path);
        // Every non-header line is well-formed (no torn writes).
        Assert.All(lines.Where(l => l.Contains("message-")),
            l => Assert.Matches(@"^\d{2}:\d{2}:\d{2}\.\d{3} \[INFO \] \[t\d\] message-\d-\d+$", l));
        Assert.Equal(400, lines.Count(l => l.Contains("message-")));
    }
}
