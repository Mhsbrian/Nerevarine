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
