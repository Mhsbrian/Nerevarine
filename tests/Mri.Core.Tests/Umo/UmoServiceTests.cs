using Mri.Core.IO;
using Mri.Core.Umo;

namespace Mri.Core.Tests.Umo;

public class UmoServiceTests
{
    private sealed class CapturingRunner : IProcessRunner
    {
        public readonly List<ProcessSpec> Specs = [];

        public Task<ProcessResult> RunAsync(
            ProcessSpec spec, IProgress<OutputLine>? onLine = null, CancellationToken ct = default)
        {
            Specs.Add(spec);
            return Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
        }
    }

    [Fact]
    public async Task EveryInvocationCarriesConfDirAndApiKeyEnv()
    {
        var runner = new CapturingRunner();
        var umo = new UmoService(runner, "/tools/umo.exe", "/conf", "the-api-key");

        await umo.AddListAsync("/modlist/list.json", "my-list");
        await umo.InstallAsync("my-list", nexusPremium: true, threads: 4);

        Assert.All(runner.Specs, spec =>
        {
            Assert.Equal("/conf", spec.Env["UMO_CONF_DIR"]);
            Assert.Equal("the-api-key", spec.Env["UMO_NEXUS_API_KEY"]);
        });
    }

    [Fact]
    public async Task MissingKeyOmitsTheEnvVar()
    {
        var runner = new CapturingRunner();
        var umo = new UmoService(runner, "/tools/umo.exe", "/conf");

        await umo.AddListAsync("/modlist/list.json", "my-list");

        Assert.False(runner.Specs[0].Env.ContainsKey("UMO_NEXUS_API_KEY"));
    }

    private sealed class ReplayRunner(IReadOnlyList<string> lines, int exitCode) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessSpec spec, IProgress<OutputLine>? onLine = null, CancellationToken ct = default)
        {
            foreach (var line in lines)
                onLine?.Report(new OutputLine(false, line));
            return Task.FromResult(new ProcessResult(exitCode, TimeSpan.Zero));
        }
    }

    [Fact]
    public async Task FailureSummaryReasonsAttachToTheirHeaderAndSurviveTheSnapshot()
    {
        // Verbatim shape of umo's end-of-run failure summary (field logs
        // 2026-08-11) — printed as the LAST lines before exit, which is why
        // parsing must be synchronous with the runner callback.
        const string esc = "\u001b";
        var runner = new ReplayRunner([
            $"{esc}[32msyncing Patch for Purists{esc}[0m",
            $"{esc}[31m- error received - skipping: Status Code 401 - b'auth'{esc}[0m",
            $"{esc}[31m152 - Traveling Merchants:{esc}[0m",
            $"{esc}[31m- error received - skipping: Status Code 404 - b'gone'{esc}[0m",
            $"{esc}[31m201 - Animated Levitation:{esc}[0m",
            $"{esc}[31m- pycurl timeout{esc}[0m",
        ], exitCode: 1);
        var umo = new UmoService(runner, "/tools/umo", "/conf", "k");

        var result = await umo.InstallAsync("my-list", nexusPremium: true, threads: 4);

        Assert.Equal(["Traveling Merchants", "Animated Levitation"], result.FailedMods);
        Assert.Equal("Status Code 404 - b'gone'", result.FailureReasons["Traveling Merchants"]);
        Assert.Equal("pycurl timeout", result.FailureReasons["Animated Levitation"]);
        // The pre-header 401 line is tallied but attached to no mod.
        Assert.Equal(3, result.ErrorLines.Count);
        Assert.False(result.Process.Success);
    }

    [Fact]
    public async Task NoModFileFoundLineIsBothFailureAndReason()
    {
        const string esc = "\u001b";
        var runner = new ReplayRunner([
            $"{esc}[31mNo mod file found for \"Some Mod/Some File\" - MOMW data may be out of date :( - skipping{esc}[0m",
        ], exitCode: 0);
        var umo = new UmoService(runner, "/tools/umo", "/conf", "k");

        var result = await umo.InstallAsync("my-list", nexusPremium: true, threads: 4);

        Assert.Equal(["Some Mod"], result.FailedMods);
        Assert.Contains("No mod file found", result.FailureReasons["Some Mod"]);
    }

    [Fact]
    public async Task InstallPassesPremiumAndThreadFlags()
    {
        var runner = new CapturingRunner();
        var umo = new UmoService(runner, "/tools/umo.exe", "/conf", "k");

        await umo.InstallAsync("my-list", nexusPremium: true, threads: 6);

        var args = runner.Specs[0].Args;
        Assert.Equal(["install", "--sync", "my-list", "--no-gui", "--verbose",
            "--nexus-premium", "--threads", "6"], args);
    }
}
