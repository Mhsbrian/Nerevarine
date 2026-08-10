using Mri.Core.IO;
using Mri.Core.Logging;

namespace Mri.Core.Tests.Logging;

public class LoggingProcessRunnerTests : IDisposable
{
    private readonly string _dir;

    public LoggingProcessRunnerTests() =>
        _dir = Directory.CreateTempSubdirectory("mri-logrunner-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class FakeRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessSpec spec, IProgress<OutputLine>? onLine = null, CancellationToken ct = default)
        {
            onLine?.Report(new OutputLine(false, "downloading mod 1/5"));
            onLine?.Report(new OutputLine(true, "warning: slow mirror"));
            return Task.FromResult(new ProcessResult(3, TimeSpan.FromSeconds(2)));
        }
    }

    [Fact]
    public async Task LogsSpawnOutputAndExitWhileForwardingToCaller()
    {
        string path;
        var forwarded = new List<OutputLine>();
        using (var log = InstallLog.CreateInDirectory(_dir, "1.0"))
        {
            path = log.FilePath;
            var runner = new LoggingProcessRunner(new FakeRunner(), log);

            var result = await runner.RunAsync(new ProcessSpec
            {
                Exe = "/tools/umo.exe",
                Args = ["install", "--sync", "my list"],
                Env = new Dictionary<string, string> { ["UMO_CONF_DIR"] = "/conf" },
            }, new SyncProgress(forwarded));

            Assert.Equal(3, result.ExitCode);
        }

        var text = File.ReadAllText(path);
        Assert.Contains("spawn: \"/tools/umo.exe\" install --sync my list", text);
        Assert.Contains("UMO_CONF_DIR=/conf", text);
        Assert.Contains("[proc:umo.exe] downloading mod 1/5", text);
        Assert.Contains("[proc:umo.exe] [stderr] warning: slow mirror", text);
        Assert.Contains("exit: umo.exe → code 3 after 2.0s", text);

        // The caller still receives every line.
        Assert.Equal(2, forwarded.Count);
    }

    private sealed class ThrowingRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessSpec spec, IProgress<OutputLine>? onLine = null, CancellationToken ct = default) =>
            throw new System.ComponentModel.Win32Exception(2, "not found");
    }

    [Fact]
    public async Task LogsFailureToStartAndRethrows()
    {
        string path;
        using (var log = InstallLog.CreateInDirectory(_dir, "1.0"))
        {
            path = log.FilePath;
            var runner = new LoggingProcessRunner(new ThrowingRunner(), log);
            await Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(
                () => runner.RunAsync(new ProcessSpec { Exe = "missing.exe" }));
        }

        Assert.Contains("missing.exe failed to run", File.ReadAllText(path));
    }

    private sealed class SyncProgress(List<OutputLine> sink) : IProgress<OutputLine>
    {
        public void Report(OutputLine value)
        {
            lock (sink)
                sink.Add(value);
        }
    }
}
