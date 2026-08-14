using System.Reflection;
using Mri.App.ViewModels;
using Mri.Core.IO;
using Mri.Core.Logging;
using Mri.Core.OpenMw;
using Mri.Core.Pipeline;

namespace Mri.App.Services;

/// <summary>Builds the InstallContext + engine from wizard choices and runs it.</summary>
public sealed class InstallRunner(WizardState state)
{
    public static string AppVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// Creates the per-run install log. Caller owns (and must dispose) it.
    /// </summary>
    public InstallLog CreateLog()
    {
        var log = InstallLog.CreateInDirectory(Path.Combine(state.InstallDir, "logs"), AppVersion);
        log.AddRedaction(state.NexusApiKey);
        return log;
    }

    public InstallEngine BuildEngine(InstallLog log, out InstallContext ctx)
    {
        log.Info("app", $"elevated: {Mri.Core.Elevation.IsElevated()}");
        if (Mri.Core.Elevation.IsElevated())
        {
            log.Error("app", "refusing to run elevated — umo exits with code 3 under admin rights");
            throw new InvalidOperationException(
                "The installer is running as administrator, which the mod downloader refuses. " +
                "Close it and start it normally (no “Run as administrator”).");
        }

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
            AppVersion = AppVersion,
            MomwContentOrder = state.Data.MomwContentOrder,
        };

        log.Info("app", $"game: '{game.Path}' (source {game.Source}, " +
                        $"tribunal={game.Validation.HasTribunal}, bloodmoon={game.Validation.HasBloodmoon})");
        log.Info("app", $"install dir: '{state.InstallDir}'");
        log.Info("app", $"modlist: {state.Data.Modlist.Name} {state.Data.Modlist.ListVersion} " +
                        $"({state.Data.Modlist.Mods.Count} mods)");
        log.Info("app", $"nexus premium: {state.NexusUser?.IsPremium ?? false}");
        log.Info("app", $"openmw config dir: '{ctx.OpenMwPaths.ConfigDir}'");
        if (ctx.State.CompletedSteps.Count > 0)
            log.Info("app", $"resuming — steps previously recorded done: " +
                            string.Join(", ", ctx.State.CompletedSteps.Keys));

        return PipelineFactory.Create(
            ctx,
            new HttpClient(),
            new LoggingProcessRunner(new ProcessRunner(), log),
            state.Data.SettingsTemplate,
            state.Data.ShadersTemplate,
            log,
            fixupsSourceDir: AppData.MaterializeFixups(state.InstallDir));
    }

    public string SaveDiagnostics() =>
        DiagnosticsBundler.CreateZip(
            state.InstallDir,
            OpenMwUserPaths.Detect().ConfigDir,
            [state.NexusApiKey]);
}
