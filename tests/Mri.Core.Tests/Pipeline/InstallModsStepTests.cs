using Mri.Core.GameDetection;
using Mri.Core.IO;
using Mri.Core.OpenMw;
using Mri.Core.Pipeline;
using Mri.Core.Pipeline.Steps;
using Mri.Core.Tools;
using Mri.Core.Umo;
using ModlistModel = Mri.Core.Modlist;

namespace Mri.Core.Tests.Pipeline;

/// <summary>
/// The failure taxonomy of the download step: N specific mods failing is a
/// per-mod retry/skip situation; the downloader itself hitting one wall for
/// everything (401/429/network) must surface as ONE cause — field-hit on
/// Linux as "586 mods failed" over a single auth error.
/// </summary>
public sealed class InstallModsStepTests : IDisposable
{
    private const string Esc = "\u001b";
    private readonly string _dir = Directory.CreateTempSubdirectory("mri-installmods-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class SimulatedUmoRunner(IReadOnlyList<string> lines, int exitCode, Action? beforeExit = null)
        : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessSpec spec, IProgress<OutputLine>? onLine = null, CancellationToken ct = default)
        {
            foreach (var line in lines)
                onLine?.Report(new OutputLine(false, line));
            beforeExit?.Invoke();
            return Task.FromResult(new ProcessResult(exitCode, TimeSpan.Zero));
        }
    }

    private static ModlistModel.Modlist MakeModlist(int count) => new()
    {
        ListVersion = "test",
        Name = "test-list",
        Mods = Enumerable.Range(1, count).Select(i => new ModlistModel.ModEntry
        {
            Id = $"mod-{i}",
            Name = $"Mod {i}",
            Category = "Patches",
            Source = new ModlistModel.ModSource
            {
                Handler = ModlistModel.ModHandler.Nexus,
                Url = $"https://www.nexusmods.com/morrowind/mods/{i}",
            },
            DataPaths = [$"Mod {i}"],
        }).ToList(),
    };

    private InstallContext MakeContext(ModlistModel.Modlist modlist) => new()
    {
        InstallDir = _dir,
        Game = new GameValidation { IsValid = true, GameRoot = "/g", DataFilesDir = "/g/Data Files" },
        Modlist = modlist,
        ToolManifest = new ToolManifest(),
        NexusApiKey = "k",
        OpenMwPaths = new OpenMwUserPaths(Path.Combine(_dir, "cfg")),
        State = new InstallState(),
        StateStore = new InstallStateStore(Path.Combine(_dir, "state.json")),
    };

    private void MaterializeModDir(InstallContext ctx, ModlistModel.ModEntry mod) =>
        Directory.CreateDirectory(Path.Combine(
            ctx.ModsRootDir, ctx.Modlist.Name,
            ModlistModel.ModlistCompiler.CategoryDir(mod.Category), mod.DataPaths[0]));

    private static Task RunStep(InstallContext ctx, IProcessRunner runner) =>
        new InstallModsStep(new UmoService(runner, "/tools/umo", "/conf", "k"))
            .RunAsync(ctx, new Progress<StepProgress>(), CancellationToken.None);

    [Fact]
    public async Task OneSharedRootCauseWithZeroProgressIsADownloaderFailureNotNModFailures()
    {
        var ctx = MakeContext(MakeModlist(6));
        var runner = new SimulatedUmoRunner(
            Enumerable.Repeat(
                $"{Esc}[31m- error received - skipping: Status Code 401 - b'auth'{Esc}[0m", 6).ToList(),
            exitCode: 1);

        var ex = await Assert.ThrowsAsync<DownloaderFailedException>(() => RunStep(ctx, runner));

        Assert.Contains("Nexus rejected the sign-in", ex.Message);
        Assert.Contains("Nothing needs skipping", ex.Message);
        Assert.Equal(6, ex.PendingCount);
        // Disk truth still records what's missing for the resume story.
        Assert.Equal(6, ctx.State.FailedMods.Count);
    }

    [Fact]
    public async Task SpecificFailuresAmongSuccessesStayPerModWithTheirReasons()
    {
        var modlist = MakeModlist(6);
        var ctx = MakeContext(modlist);
        var runner = new SimulatedUmoRunner([
            $"{Esc}[31m2 - Mod 2:{Esc}[0m",
            $"{Esc}[31m- error received - skipping: Status Code 404 - b'gone'{Esc}[0m",
            $"{Esc}[31m5 - Mod 5:{Esc}[0m",
            $"{Esc}[31m- pycurl timeout{Esc}[0m",
        ], exitCode: 1, beforeExit: () =>
        {
            foreach (var mod in modlist.Mods.Where(m => m.Id is not ("mod-2" or "mod-5")))
                MaterializeModDir(ctx, mod);
        });

        var ex = await Assert.ThrowsAsync<ModsFailedException>(() => RunStep(ctx, runner));

        Assert.Equal(2, ex.Failures.Count);
        Assert.Equal("Status Code 404 - b'gone'",
            ex.Failures.Single(f => f.Name == "Mod 2").Reason);
        Assert.Equal("pycurl timeout",
            ex.Failures.Single(f => f.Name == "Mod 5").Reason);
    }

    [Fact]
    public async Task DominantErrorWithRealProgressIsStillPerMod()
    {
        // Half the list arrived before the wall — the user must see exactly
        // which mods are missing, not a blanket downloader error.
        var modlist = MakeModlist(6);
        var ctx = MakeContext(modlist);
        var runner = new SimulatedUmoRunner(
            Enumerable.Repeat($"{Esc}[31m- error received - skipping: Status Code 429{Esc}[0m", 5).ToList(),
            exitCode: 1, beforeExit: () =>
            {
                foreach (var mod in modlist.Mods.Take(3))
                    MaterializeModDir(ctx, mod);
            });

        var ex = await Assert.ThrowsAsync<ModsFailedException>(() => RunStep(ctx, runner));

        Assert.Equal(3, ex.Failures.Count);
    }

    [Fact]
    public async Task NexusWallWithNonNexusProgressIsStillTheDownloaderFailing()
    {
        // The real field shape: the ~30 github/gitlab/direct mods sail
        // through a Nexus wall, so progress is never zero. That must not
        // demote a 401 wall to a 500-row per-mod list.
        var modlist = MakeModlist(40);
        var ctx = MakeContext(modlist);
        var runner = new SimulatedUmoRunner(
            Enumerable.Repeat(
                $"{Esc}[31m- error received - skipping: Status Code 401 - b'auth'{Esc}[0m", 28).ToList(),
            exitCode: 1, beforeExit: () =>
            {
                foreach (var mod in modlist.Mods.Take(12))
                    MaterializeModDir(ctx, mod);
            });

        var ex = await Assert.ThrowsAsync<DownloaderFailedException>(() => RunStep(ctx, runner));

        Assert.Contains("Nexus rejected the sign-in", ex.Message);
        Assert.Contains("12 mods made it", ex.Message);
        Assert.Equal(28, ex.PendingCount);
    }

    [Fact]
    public async Task DiskFullIsNamedAsTheSharedWall()
    {
        var ctx = MakeContext(MakeModlist(30));
        var runner = new SimulatedUmoRunner(
            Enumerable.Repeat(
                $"{Esc}[31m- error received - skipping: [Errno 28] No space left on device{Esc}[0m", 30).ToList(),
            exitCode: 1);

        var ex = await Assert.ThrowsAsync<DownloaderFailedException>(() => RunStep(ctx, runner));

        Assert.Contains("disk filled up", ex.Message);
    }

    [Fact]
    public async Task CleanExitWithMassMissingIsReportedAsOurBugNotTheirMods()
    {
        // umo says success, prints no errors, yet nothing is where the
        // verifier looks: a layout-expectation mismatch — an installer bug
        // that must ask for diagnostics, never render 500 failure rows.
        var ctx = MakeContext(MakeModlist(30));
        var runner = new SimulatedUmoRunner([], exitCode: 0);

        var ex = await Assert.ThrowsAsync<DownloaderFailedException>(() => RunStep(ctx, runner));

        Assert.Contains("bug on our side", ex.Message);
        Assert.Contains("diagnostics", ex.Message);
    }

    [Fact]
    public async Task EverythingArrivedDespiteNonZeroExitIsAPlainProcessError()
    {
        var modlist = MakeModlist(3);
        var ctx = MakeContext(modlist);
        var runner = new SimulatedUmoRunner([], exitCode: 2, beforeExit: () =>
        {
            foreach (var mod in modlist.Mods)
                MaterializeModDir(ctx, mod);
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunStep(ctx, runner));
        Assert.Contains("exited with code 2", ex.Message);
    }
}
