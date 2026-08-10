using Mri.Core.IO;

namespace Mri.Core.Tests.IO;

public class ProcessRunnerTests
{
    /// <summary>
    /// Synchronous, locked IProgress: Progress&lt;T&gt; posts to a sync context
    /// asynchronously, which both races the assertions and makes List.Add
    /// unsafe across the two pump tasks.
    /// </summary>
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly List<T> _items = [];

        public void Report(T value)
        {
            lock (_items)
                _items.Add(value);
        }

        public IReadOnlyList<T> Items
        {
            get
            {
                lock (_items)
                    return _items.ToList();
            }
        }
    }

    private static ProcessSpec Shell(string script) =>
        OperatingSystem.IsWindows()
            ? new ProcessSpec { Exe = "cmd.exe", Args = ["/c", script] }
            : new ProcessSpec { Exe = "/bin/sh", Args = ["-c", script] };

    [Fact]
    public async Task CapturesStdoutStderrAndExitCode()
    {
        var progress = new SyncProgress<OutputLine>();
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(
            Shell("echo out && echo err 1>&2 && exit 3"),
            progress);

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.Success);
        Assert.Contains(progress.Items, l => !l.IsError && l.Text.Trim() == "out");
        Assert.Contains(progress.Items, l => l.IsError && l.Text.Trim() == "err");
    }

    [Fact]
    public async Task PassesEnvironmentVariables()
    {
        var progress = new SyncProgress<OutputLine>();
        var runner = new ProcessRunner();
        var spec = Shell(OperatingSystem.IsWindows() ? "echo %MRI_TEST_VAR%" : "echo $MRI_TEST_VAR") with
        {
            Env = new Dictionary<string, string> { ["MRI_TEST_VAR"] = "hello-env" },
        };

        var result = await runner.RunAsync(spec, progress);

        Assert.True(result.Success);
        Assert.Contains(progress.Items, l => l.Text.Contains("hello-env"));
    }

    [Fact]
    public async Task RawArgumentsBypassAutoQuoting()
    {
        var progress = new SyncProgress<OutputLine>();
        var runner = new ProcessRunner();
        var spec = OperatingSystem.IsWindows()
            ? new ProcessSpec { Exe = "cmd.exe", RawArguments = "/c echo one two" }
            : new ProcessSpec { Exe = "/bin/echo", RawArguments = "one two" };

        var result = await runner.RunAsync(spec, progress);

        Assert.True(result.Success);
        // The raw string is parsed as separate args, not one quoted blob.
        Assert.Contains(progress.Items, l => l.Text.Trim() == "one two");
    }

    [Fact]
    public async Task TimeoutKillsProcess()
    {
        var runner = new ProcessRunner();
        var spec = Shell(OperatingSystem.IsWindows() ? "ping -n 30 127.0.0.1 > nul" : "sleep 30") with
        {
            Timeout = TimeSpan.FromMilliseconds(300),
        };

        await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(spec));
    }
}
