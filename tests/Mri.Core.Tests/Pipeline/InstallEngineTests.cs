using Mri.Core.GameDetection;
using Mri.Core.Modlist;
using Mri.Core.OpenMw;
using Mri.Core.Pipeline;
using Mri.Core.Tools;

namespace Mri.Core.Tests.Pipeline;

public class InstallEngineTests : IDisposable
{
    private readonly string _dir;

    public InstallEngineTests() =>
        _dir = Directory.CreateTempSubdirectory("mri-engine-test-").FullName;

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
            OpenMwPaths = new OpenMwUserPaths(Path.Combine(_dir, "openmw-config")),
            State = stateStore.Load(),
            StateStore = stateStore,
        };
    }

    private sealed class FakeStep(
        string id,
        Func<InstallContext, bool> verify,
        Func<InstallContext, Task>? run = null,
        bool optional = false) : IInstallStep
    {
        public int RunCount;
        public string Id => id;
        public string Label => id;
        public bool IsOptional => optional;

        public bool Verify(InstallContext ctx) => verify(ctx);

        public async Task RunAsync(InstallContext ctx, IProgress<StepProgress> progress, CancellationToken ct)
        {
            RunCount++;
            if (run is not null)
                await run(ctx);
        }
    }

    [Fact]
    public async Task RunsStepsInOrderAndPersistsState()
    {
        var order = new List<string>();
        var done = new HashSet<string>();
        var ctx = MakeContext();

        FakeStep Step(string id) => new(
            id,
            _ => done.Contains(id),
            _ =>
            {
                order.Add(id);
                done.Add(id);
                return Task.CompletedTask;
            });

        var engine = new InstallEngine([Step("a"), Step("b"), Step("c")]);
        var result = await engine.RunAsync(ctx);

        Assert.True(result.Success);
        Assert.Equal(["a", "b", "c"], order);
        Assert.Equal(["a", "b", "c"], ctx.StateStore.Load().CompletedSteps.Keys.Order().ToList());
    }

    [Fact]
    public async Task VerifiedStepsAreSkipped()
    {
        var ctx = MakeContext();
        var alreadyDone = new FakeStep("done", _ => true);
        var pending = new FakeStep("pending", _ => false, _ => Task.CompletedTask);

        // A run-once step: verify passes only after RunAsync flipped the flag.
        var flag = false;
        var runOnce = new FakeStep("run-once", _ => flag, _ =>
        {
            flag = true;
            return Task.CompletedTask;
        });

        var engine = new InstallEngine([alreadyDone, runOnce, pending]);
        await engine.RunAsync(ctx);

        Assert.Equal(0, alreadyDone.RunCount);
        Assert.Equal(1, runOnce.RunCount);
    }

    [Fact]
    public async Task FailingStepStopsTheRunAndReportsIt()
    {
        var ctx = MakeContext();
        var reports = new List<EngineProgress>();
        var never = new FakeStep("never", _ => false, _ => Task.CompletedTask);
        var failing = new FakeStep("failing", _ => false,
            _ => throw new InvalidOperationException("boom"));

        var engine = new InstallEngine([failing, never]);
        var result = await engine.RunAsync(ctx, new SyncProgress(reports));

        Assert.False(result.Success);
        Assert.Equal("failing", result.FailedStepId);
        Assert.IsType<InvalidOperationException>(result.Error);
        Assert.Equal(0, never.RunCount);
        Assert.Contains(reports, r => r.StepId == "failing" && r.Status == StepStatus.Failed);
    }

    [Fact]
    public async Task StepThatLiesAboutSuccessFailsVerification()
    {
        var ctx = MakeContext();
        var liar = new FakeStep("liar", _ => false, _ => Task.CompletedTask);

        var result = await new InstallEngine([liar]).RunAsync(ctx);

        Assert.False(result.Success);
        Assert.Contains("failed verification", result.Error!.Message);
    }

    [Fact]
    public async Task OptionalStepFailureDoesNotStopTheRun()
    {
        var ctx = MakeContext();
        var optional = new FakeStep("optional", _ => false,
            _ => throw new InvalidOperationException("meh"), optional: true);
        var afterFlag = false;
        var after = new FakeStep("after", _ => afterFlag, _ =>
        {
            afterFlag = true;
            return Task.CompletedTask;
        });

        var result = await new InstallEngine([optional, after]).RunAsync(ctx);

        Assert.True(result.Success);
        Assert.Equal(1, after.RunCount);
    }

    [Fact]
    public async Task ResumeSkipsCompletedWork()
    {
        var ctx = MakeContext();
        var completedOnDisk = new HashSet<string>();

        FakeStep Step(string id) => new(
            id,
            _ => completedOnDisk.Contains(id),
            _ =>
            {
                completedOnDisk.Add(id);
                return Task.CompletedTask;
            });

        var first = Step("one");
        var crash = new FakeStep("two", _ => completedOnDisk.Contains("two"),
            _ => throw new InvalidOperationException("network died"));

        var run1 = await new InstallEngine([first, crash]).RunAsync(ctx);
        Assert.False(run1.Success);
        Assert.Equal(1, first.RunCount);

        // "Fix the network" and resume with a fresh context (fresh app start).
        var resumedCtx = MakeContext();
        var second = Step("two");
        var run2 = await new InstallEngine([Step("one"), second]).RunAsync(resumedCtx);

        Assert.True(run2.Success);
        Assert.Equal(1, second.RunCount);
    }

    private sealed class SyncProgress(List<EngineProgress> sink) : IProgress<EngineProgress>
    {
        public void Report(EngineProgress value)
        {
            lock (sink)
                sink.Add(value);
        }
    }
}
