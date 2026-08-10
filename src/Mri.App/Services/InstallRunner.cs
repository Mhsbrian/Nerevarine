using Mri.App.ViewModels;
using Mri.Core.IO;
using Mri.Core.OpenMw;
using Mri.Core.Pipeline;

namespace Mri.App.Services;

/// <summary>Builds the InstallContext + engine from wizard choices and runs it.</summary>
public sealed class InstallRunner(WizardState state)
{
    public InstallEngine BuildEngine(out InstallContext ctx)
    {
        var game = state.Game
            ?? throw new InvalidOperationException("No game selected.");
        Directory.CreateDirectory(state.InstallDir);

        var stateStore = new InstallStateStore(Path.Combine(state.InstallDir, "state.json"));
        ctx = new InstallContext
        {
            InstallDir = state.InstallDir,
            Game = game.Validation,
            Modlist = state.Data.Modlist,
            ToolManifest = state.Data.ToolManifest,
            NexusApiKey = state.NexusApiKey,
            OpenMwPaths = OpenMwUserPaths.Detect(),
            State = stateStore.Load(),
            StateStore = stateStore,
        };

        return PipelineFactory.Create(
            ctx,
            new HttpClient(),
            new ProcessRunner(),
            state.Data.SettingsTemplate,
            state.Data.ShadersTemplate);
    }
}
