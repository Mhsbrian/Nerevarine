using System.Security.Cryptography;
using System.Text;
using Mri.Core.IO;
using Mri.Core.Modlist;
using Mri.Core.OpenMw;
using Mri.Core.Tools;
using Mri.Core.Umo;

namespace Mri.Core.Pipeline.Steps;

public sealed class AcquireToolsStep(ToolAcquisitionService tools) : IInstallStep
{
    public string Id => "acquire-tools";
    public string Label => "Download OpenMW and modding tools";

    public bool Verify(InstallContext ctx) =>
        ctx.ToolManifest.Tools.All(tools.IsInstalled);

    public async Task RunAsync(InstallContext ctx, IProgress<StepProgress> progress, CancellationToken ct)
    {
        // Downloads report every buffer chunk; forward only whole-percent
        // changes so the install log stays readable (a 60 MB download once
        // produced 8,000 identical "0%" lines).
        var lastReported = new Dictionary<string, int>();
        var toolProgress = new Progress<ToolProgress>(p =>
        {
            var fraction = p.BytesTotal is > 0 ? (double?)p.BytesDone / p.BytesTotal : null;
            var percent = fraction is { } f ? (int)(f * 100) : -1;
            var key = $"{p.ToolId}:{p.Phase}";
            if (lastReported.TryGetValue(key, out var previous) && previous == percent)
                return;
            lastReported[key] = percent;
            progress.Report(new StepProgress($"{p.ToolId}: {p.Phase}", fraction));
        });
        await tools.EnsureAllAsync(ctx.ToolManifest, toolProgress, ct).ConfigureAwait(false);
    }
}

public sealed class WriteUmoConfigStep(UmoConfigWriter writer, Func<InstallContext, UmoSettings> settings)
    : IInstallStep
{
    public string Id => "write-umo-config";
    public string Label => "Configure the mod downloader";

    // Content comparison: an existing config written by an older build (or
    // edited by hand) must be regenerated, not trusted for merely existing.
    public bool Verify(InstallContext ctx) => writer.IsCurrent(settings(ctx));

    public Task RunAsync(InstallContext ctx, IProgress<StepProgress> progress, CancellationToken ct)
    {
        writer.Write(settings(ctx));
        return Task.CompletedTask;
    }
}

public sealed class RegisterModlistStep(UmoService umo) : IInstallStep
{
    public string Id => "register-modlist";
    public string Label => "Register the modlist with the downloader";

    private static string RegisteredMarkerPath(InstallContext ctx) =>
        ctx.EmittedUmoListPath + ".registered";

    private static string EmitJson(InstallContext ctx) =>
        ModlistCompiler.ToUmoModDescJson(ctx.Modlist);

    private static string HashOf(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    public bool Verify(InstallContext ctx)
    {
        if (!File.Exists(ctx.EmittedUmoListPath) || !File.Exists(RegisteredMarkerPath(ctx)))
            return false;
        var currentHash = HashOf(EmitJson(ctx));
        return File.ReadAllText(ctx.EmittedUmoListPath) == EmitJson(ctx) &&
               File.ReadAllText(RegisteredMarkerPath(ctx)).Trim() == currentHash;
    }

    public async Task RunAsync(InstallContext ctx, IProgress<StepProgress> progress, CancellationToken ct)
    {
        var json = EmitJson(ctx);
        Directory.CreateDirectory(ctx.ModlistDir);
        AtomicFile.WriteAllText(ctx.EmittedUmoListPath, json);

        progress.Report(new StepProgress("Registering modlist with umo…"));
        await umo.AddListAsync(ctx.EmittedUmoListPath, ctx.UmoListName, null, ct).ConfigureAwait(false);

        AtomicFile.WriteAllText(RegisteredMarkerPath(ctx), HashOf(json));
    }
}

public sealed class InstallModsStep(UmoService umo) : IInstallStep
{
    public string Id => "install-mods";
    public string Label => "Download and install all mods";

    // Disk truth: every non-skipped mod with data paths has its first data
    // dir on disk, and no unskipped failures remain recorded. Deliberately
    // NOT conditioned on CompletedSteps — the engine records completion only
    // AFTER post-run Verify passes, so that would deadlock first success.
    public bool Verify(InstallContext ctx) =>
        !PendingMods(ctx).Any() &&
        !ctx.State.FailedMods.Except(ctx.State.SkippedMods).Any();

    private static IEnumerable<ModEntry> PendingMods(InstallContext ctx) =>
        ctx.Modlist.Mods
            .Where(m => !ctx.State.SkippedMods.Contains(m.Id))
            .Where(m => m.DataPaths.Count > 0)
            // umo's layout is BASEPATH/<list>/<category>/<extract_to> — the
            // verifier must look exactly where umo extracts (field-verified).
            .Where(m => !Directory.Exists(Path.Combine(
                ctx.ModsRootDir, ctx.Modlist.Name,
                ModlistCompiler.CategoryDir(m.Category), m.DataPaths[0])));

    public async Task RunAsync(InstallContext ctx, IProgress<StepProgress> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(ctx.ModsRootDir);

        var total = ctx.Modlist.Mods.Count;
        var result = await umo.InstallAsync(
            ctx.UmoListName,
            ctx.NexusPremium,
            ctx.DownloadThreads,
            new Progress<UmoEvent>(evt =>
            {
                var fraction = evt is { Current: { } current, Total: > 0 }
                    ? (double?)current / evt.Total
                    : null;
                var message = evt.Kind switch
                {
                    UmoEventKind.Download => $"Downloading {evt.ModName}",
                    UmoEventKind.Extract => $"Installing {evt.ModName}",
                    UmoEventKind.ModFailed => $"FAILED: {evt.ModName}",
                    _ => evt.RawLine,
                };
                progress.Report(new StepProgress(message, fraction));
            }),
            ct).ConfigureAwait(false);

        // Disk truth is the authority on what failed: any non-skipped mod
        // whose data dir never appeared. Parser events feed the live UI, but
        // umo's prose must never decide the failure list (it once produced a
        // state file full of mod-description fragments).
        var missing = PendingMods(ctx).Select(m => m.Name).Distinct().ToList();
        ctx.State.FailedMods = missing;
        ctx.SaveState();

        if (!result.Process.Success && missing.Count == 0)
            throw new InvalidOperationException(
                $"umo install exited with code {result.Process.ExitCode}.");
        if (missing.Count > 0)
            throw new ModsFailedException(missing);
    }
}

public sealed class ImportIniStep(IniImporterService importer, Func<InstallContext, string?> iniImporterExe)
    : IInstallStep
{
    public string Id => "import-ini";
    public string Label => "Import Morrowind.ini settings";

    public bool Verify(InstallContext ctx) => ctx.State.FallbackLines.Count > 0;

    public async Task RunAsync(InstallContext ctx, IProgress<StepProgress> progress, CancellationToken ct)
    {
        var exe = iniImporterExe(ctx)
            ?? throw new InvalidOperationException("openmw-iniimporter.exe not found in the OpenMW install.");
        var ini = ctx.Game.MorrowindIniPath
            ?? throw new InvalidOperationException(
                "Morrowind.ini was not found next to the game — run the vanilla game once, then retry.");

        progress.Report(new StepProgress("Converting Morrowind.ini to OpenMW fallback settings…"));
        var lines = await importer.ImportFallbackLinesAsync(exe, ini, ct: ct).ConfigureAwait(false);
        if (lines.Count == 0)
            throw new InvalidOperationException("openmw-iniimporter produced no fallback settings.");

        ctx.State.FallbackLines = lines.ToList();
        ctx.SaveState();
    }
}

public sealed class GenerateOpenMwCfgStep : IInstallStep
{
    public string Id => "generate-openmw-cfg";
    public string Label => "Generate openmw.cfg";

    public bool Verify(InstallContext ctx)
    {
        if (!File.Exists(ctx.OpenMwPaths.OpenMwCfgPath))
            return false;
        var text = File.ReadAllText(ctx.OpenMwPaths.OpenMwCfgPath);
        // Phase 2 (delta included) rewrites this file; both shapes are "current".
        return OpenMwCfgComposer.IsCurrent(text, ctx.CfgComposition(includeDelta: false)) ||
               OpenMwCfgComposer.IsCurrent(text, ctx.CfgComposition(includeDelta: true));
    }

    public Task RunAsync(InstallContext ctx, IProgress<StepProgress> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(ctx.OpenMwPaths.ConfigDir);
        var cfgPath = ctx.OpenMwPaths.OpenMwCfgPath;

        if (File.Exists(cfgPath) && !File.ReadAllText(cfgPath).StartsWith(OpenMwCfgComposer.HeaderPrefix))
        {
            var backup = cfgPath + $".bak-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
            File.Copy(cfgPath, backup, overwrite: false);
            progress.Report(new StepProgress($"Backed up existing openmw.cfg to {Path.GetFileName(backup)}"));
        }

        AtomicFile.WriteAllText(cfgPath, OpenMwCfgComposer.Compose(ctx.CfgComposition(includeDelta: false)));
        return Task.CompletedTask;
    }
}

public sealed class GenerateSettingsStep(string settingsTemplate, string shadersTemplate) : IInstallStep
{
    public string Id => "generate-settings";
    public string Label => "Apply tuned graphics settings";

    public bool Verify(InstallContext ctx) =>
        File.Exists(ctx.OpenMwPaths.SettingsCfgPath) &&
        SettingsCfgMerger.IsApplied(File.ReadAllText(ctx.OpenMwPaths.SettingsCfgPath), settingsTemplate) &&
        File.Exists(ctx.OpenMwPaths.ShadersYamlPath);

    public Task RunAsync(InstallContext ctx, IProgress<StepProgress> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(ctx.OpenMwPaths.ConfigDir);

        var existing = File.Exists(ctx.OpenMwPaths.SettingsCfgPath)
            ? File.ReadAllText(ctx.OpenMwPaths.SettingsCfgPath)
            : string.Empty;
        var merged = SettingsCfgMerger.Merge(existing, settingsTemplate);
        AtomicFile.WriteAllText(ctx.OpenMwPaths.SettingsCfgPath, merged.Text);
        progress.Report(new StepProgress($"settings.cfg: {merged.ChangedKeys} keys updated"));

        AtomicFile.WriteAllText(ctx.OpenMwPaths.ShadersYamlPath, shadersTemplate);
        return Task.CompletedTask;
    }
}

public sealed class DeltaMergeStep(DeltaPluginService delta, Func<InstallContext, string?> deltaExe)
    : IInstallStep
{
    public string Id => "delta-merge";
    public string Label => "Merge leveled lists (delta-plugin)";

    private static string OutputPath(InstallContext ctx) =>
        Path.Combine(ctx.ModsRootDir, "delta-merged", "delta-merged.omwaddon");

    public bool Verify(InstallContext ctx) =>
        File.Exists(OutputPath(ctx)) &&
        File.Exists(ctx.OpenMwPaths.OpenMwCfgPath) &&
        OpenMwCfgComposer.IsCurrent(
            File.ReadAllText(ctx.OpenMwPaths.OpenMwCfgPath),
            ctx.CfgComposition(includeDelta: true));

    public async Task RunAsync(InstallContext ctx, IProgress<StepProgress> progress, CancellationToken ct)
    {
        var exe = deltaExe(ctx)
            ?? throw new InvalidOperationException("delta_plugin.exe not found in the tools pack.");

        progress.Report(new StepProgress("Merging objects and leveled lists…"));
        await delta.MergeAsync(exe, ctx.OpenMwPaths.ConfigDir, OutputPath(ctx),
            new Progress<OutputLine>(l => progress.Report(new StepProgress(l.Text))), ct)
            .ConfigureAwait(false);

        progress.Report(new StepProgress("Activating merged addon in openmw.cfg…"));
        AtomicFile.WriteAllText(
            ctx.OpenMwPaths.OpenMwCfgPath,
            OpenMwCfgComposer.Compose(ctx.CfgComposition(includeDelta: true)));
    }
}

public sealed class NavmeshStep(NavmeshService navmesh, Func<InstallContext, string?> navmeshExe)
    : IInstallStep
{
    public string Id => "navmesh";
    public string Label => "Pre-build navigation meshes";

    // The navmesh db lands in OpenMW's user-data dir, which varies; the
    // completion record is the only cheap signal. Re-running is always safe.
    public bool Verify(InstallContext ctx) => ctx.State.CompletedSteps.ContainsKey(Id);

    public bool VerifyAfterRun => false;

    public async Task RunAsync(InstallContext ctx, IProgress<StepProgress> progress, CancellationToken ct)
    {
        var exe = navmeshExe(ctx)
            ?? throw new InvalidOperationException("openmw-navmeshtool.exe not found in the OpenMW install.");

        progress.Report(new StepProgress("Building navigation meshes (this can take a long while)…"));
        await navmesh.GenerateAsync(exe, ctx.OpenMwPaths.ConfigDir,
            new Progress<OutputLine>(l => progress.Report(new StepProgress(l.Text))), ct)
            .ConfigureAwait(false);
    }
}

public sealed class ValidateStep(IProcessRunner runner, Func<InstallContext, string?> validatorExe)
    : IInstallStep
{
    public string Id => "validate";
    public string Label => "Validate the installation (optional)";
    public bool IsOptional => true;

    public bool Verify(InstallContext ctx) => ctx.State.CompletedSteps.ContainsKey(Id);

    public bool VerifyAfterRun => false;

    public async Task RunAsync(InstallContext ctx, IProgress<StepProgress> progress, CancellationToken ct)
    {
        var exe = validatorExe(ctx);
        if (exe is null)
        {
            progress.Report(new StepProgress("openmw-validator not present — skipping."));
            return;
        }

        var result = await runner.RunAsync(new ProcessSpec
        {
            Exe = exe,
            WorkingDir = ctx.OpenMwPaths.ConfigDir,
            Env = new Dictionary<string, string> { ["OPENMW_CONFIG"] = ctx.OpenMwPaths.ConfigDir },
            Timeout = TimeSpan.FromMinutes(30),
        }, new Progress<OutputLine>(l => progress.Report(new StepProgress(l.Text))), ct)
            .ConfigureAwait(false);

        if (!result.Success)
            throw new InvalidOperationException($"openmw-validator reported problems (exit {result.ExitCode}).");
    }
}
