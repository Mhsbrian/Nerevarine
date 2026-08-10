using Mri.Core.GameDetection;
using Mri.Core.Logging;
using Mri.Core.OpenMw;
using Mri.Core.Pipeline;
using Mri.Core.Tools;

namespace Mri.Core.Tests.Logging;

public class EngineLoggingTests : IDisposable
{
    private readonly string _dir;

    public EngineLoggingTests() =>
        _dir = Directory.CreateTempSubdirectory("mri-enginelog-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private InstallContext MakeContext()
    {
        var stateStore = new InstallStateStore(Path.Combine(_dir, "state.json"));
        return new InstallContext
        {
            InstallDir = _dir,
            Game = new GameValidation { IsValid = true, GameRoot = _dir, DataFilesDir = _dir },
            Modlist = new Mri.Core.Modlist.Modlist { ListVersion = "1.0", Name = "test" },
            ToolManifest = new ToolManifest(),
            NexusApiKey = "key",
            OpenMwPaths = new OpenMwUserPaths(Path.Combine(_dir, "cfg")),
            State = stateStore.Load(),
            StateStore = stateStore,
        };
    }

    private sealed class Step(string id, Func<InstallContext, bool> verify, Func<Task>? run = null)
        : IInstallStep
    {
        public string Id => id;
        public string Label => id;
        public bool Verify(InstallContext ctx) => verify(ctx);

        public async Task RunAsync(InstallContext ctx, IProgress<StepProgress> progress, CancellationToken ct)
        {
            if (run is not null)
                await run();
        }
    }

    [Fact]
    public async Task RunLifecycleIsFullyLogged()
    {
        string path;
        using (var log = InstallLog.CreateInDirectory(Path.Combine(_dir, "logs"), "1.0"))
        {
            path = log.FilePath;
            var done = false;
            var engine = new InstallEngine(
            [
                new Step("skipped", _ => true),
                new Step("worker", _ => done, () =>
                {
                    done = true;
                    return Task.CompletedTask;
                }),
                new Step("bomb", _ => false, () => throw new InvalidOperationException("network died")),
            ], log);

            var result = await engine.RunAsync(MakeContext());
            Assert.False(result.Success);
        }

        var text = File.ReadAllText(path);
        Assert.Contains("=== run started", text);
        Assert.Contains("[skipped] already satisfied on disk — skipping", text);
        Assert.Contains("[worker] starting", text);
        Assert.Contains("[worker] completed and verified", text);
        Assert.Contains("[bomb] step failed", text);
        Assert.Contains("network died", text);
        Assert.Contains("=== run FAILED at step 'bomb' ===", text);
    }

    [Fact]
    public async Task ThrowingPreVerifyIsLoggedAndTreatedAsNotDone()
    {
        string path;
        using (var log = InstallLog.CreateInDirectory(Path.Combine(_dir, "logs"), "1.0"))
        {
            path = log.FilePath;
            var ran = false;
            var flaky = new Step("flaky",
                _ => ran ? true : throw new IOException("transient disk hiccup"),
                () =>
                {
                    ran = true;
                    return Task.CompletedTask;
                });

            var result = await new InstallEngine([flaky], log).RunAsync(MakeContext());

            Assert.True(result.Success);
            Assert.True(ran);
        }

        Assert.Contains("pre-run verify threw", File.ReadAllText(path));
    }
}
