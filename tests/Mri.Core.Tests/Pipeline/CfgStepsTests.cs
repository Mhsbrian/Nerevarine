using Mri.Core.GameDetection;
using Mri.Core.Modlist;
using Mri.Core.OpenMw;
using Mri.Core.Pipeline;
using Mri.Core.Pipeline.Steps;
using Mri.Core.Tools;

namespace Mri.Core.Tests.Pipeline;

/// <summary>
/// End-to-end (minus real tools) coverage of the config-generation steps:
/// phase-1 cfg → settings overlay → phase-2 rewrite after a (faked) delta run.
/// </summary>
public class CfgStepsTests : IDisposable
{
    private readonly string _dir;

    public CfgStepsTests() =>
        _dir = Directory.CreateTempSubdirectory("mri-cfgsteps-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private InstallContext MakeContext()
    {
        var gameDir = Path.Combine(_dir, "game");
        Directory.CreateDirectory(Path.Combine(gameDir, "Data Files"));

        var stateStore = new InstallStateStore(Path.Combine(_dir, "state.json"));
        var state = stateStore.Load();
        state.FallbackLines = ["FontColor_color_normal,202,165,96"];

        return new InstallContext
        {
            InstallDir = _dir,
            Game = new GameValidation
            {
                IsValid = true,
                GameRoot = gameDir,
                DataFilesDir = Path.Combine(gameDir, "Data Files"),
                HasTribunal = true,
                HasBloodmoon = true,
            },
            Modlist = new Mri.Core.Modlist.Modlist
            {
                ListVersion = "2026.08.0",
                Name = "test-list",
                Mods =
                [
                    new ModEntry
                    {
                        Id = "mod-a",
                        Name = "Mod A",
                        Category = "Test",
                        Source = new ModSource { Handler = ModHandler.Direct, Url = "https://x" },
                        DataPaths = ["ModA"],
                        Content = [new ContentFile { File = "ModA.esp" }],
                    },
                ],
            },
            ToolManifest = new ToolManifest(),
            NexusApiKey = "key",
            OpenMwPaths = new OpenMwUserPaths(Path.Combine(_dir, "openmw-config")),
            State = state,
            StateStore = stateStore,
        };
    }

    [Fact]
    public async Task GenerateCfgBacksUpForeignFileAndWritesPhase1()
    {
        var ctx = MakeContext();
        Directory.CreateDirectory(ctx.OpenMwPaths.ConfigDir);
        File.WriteAllText(ctx.OpenMwPaths.OpenMwCfgPath, "data=\"C:/old/user/setup\"\n");

        var step = new GenerateOpenMwCfgStep();
        Assert.False(step.Verify(ctx));
        await step.RunAsync(ctx, new Progress<StepProgress>(), CancellationToken.None);

        Assert.True(step.Verify(ctx));
        var text = File.ReadAllText(ctx.OpenMwPaths.OpenMwCfgPath);
        Assert.StartsWith(OpenMwCfgComposer.HeaderPrefix, text);
        Assert.Contains("content=ModA.esp", text);
        Assert.DoesNotContain("delta-merged", text);

        // The user's old cfg survived as a backup.
        Assert.Single(Directory.GetFiles(ctx.OpenMwPaths.ConfigDir, "openmw.cfg.bak-*"));
    }

    [Fact]
    public async Task SettingsStepMergesTemplateAndShipsShaders()
    {
        var ctx = MakeContext();
        const string settingsTemplate = "[Camera]\nviewing distance = 81920\n";
        const string shadersTemplate = "config: {}\n";

        var step = new GenerateSettingsStep(settingsTemplate, shadersTemplate);
        Assert.False(step.Verify(ctx));
        await step.RunAsync(ctx, new Progress<StepProgress>(), CancellationToken.None);

        Assert.True(step.Verify(ctx));
        Assert.Contains("viewing distance = 81920", File.ReadAllText(ctx.OpenMwPaths.SettingsCfgPath));
        Assert.Equal(shadersTemplate, File.ReadAllText(ctx.OpenMwPaths.ShadersYamlPath));
    }

    [Fact]
    public async Task DeltaStepRewritesCfgToPhase2()
    {
        var ctx = MakeContext();

        // Phase 1 first.
        await new GenerateOpenMwCfgStep().RunAsync(ctx, new Progress<StepProgress>(), CancellationToken.None);

        // Fake delta service: drop the omwaddon where the real tool would.
        var fakeDelta = new DeltaPluginService(new FakeRunner(() =>
        {
            var output = Path.Combine(ctx.ModsRootDir, "delta-merged", "delta-merged.omwaddon");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, "merged");
        }));

        var step = new DeltaMergeStep(fakeDelta, _ => "/fake/delta_plugin");
        Assert.False(step.Verify(ctx));
        await step.RunAsync(ctx, new Progress<StepProgress>(), CancellationToken.None);

        Assert.True(step.Verify(ctx));
        var text = File.ReadAllText(ctx.OpenMwPaths.OpenMwCfgPath);
        Assert.Contains("content=delta-merged.omwaddon", text);
        Assert.Contains("delta-merged", string.Join("\n", OpenMwCfg.Parse(text).GetValues("data")));

        // Phase-1 generator still verifies (either-phase acceptance).
        Assert.True(new GenerateOpenMwCfgStep().Verify(ctx));
    }

    private sealed class FakeRunner(Action onRun) : Mri.Core.IO.IProcessRunner
    {
        public Task<Mri.Core.IO.ProcessResult> RunAsync(
            Mri.Core.IO.ProcessSpec spec,
            IProgress<Mri.Core.IO.OutputLine>? onLine = null,
            CancellationToken ct = default)
        {
            onRun();
            return Task.FromResult(new Mri.Core.IO.ProcessResult(0, TimeSpan.Zero));
        }
    }
}
