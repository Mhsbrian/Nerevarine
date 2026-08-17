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

public sealed class RegisterModlistStep(UmoService umo, EphemeralUrlResolver? urlResolver = null)
    : IInstallStep
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
        // The marker hash covers the PURE emitted list; the file on disk may
        // additionally carry resolved ephemeral URLs (EphemeralUrlResolver),
        // so its content is deliberately not compared byte-for-byte.
        if (!File.Exists(ctx.EmittedUmoListPath) || !File.Exists(RegisteredMarkerPath(ctx)))
            return false;
        return File.ReadAllText(RegisteredMarkerPath(ctx)).Trim() == HashOf(EmitJson(ctx));
    }

    public async Task RunAsync(InstallContext ctx, IProgress<StepProgress> progress, CancellationToken ct)
    {
        var json = EmitJson(ctx);
        var registered = json;
        if (urlResolver is not null)
        {
            registered = await urlResolver.ResolveAsync(json, ct).ConfigureAwait(false);
            if (!ReferenceEquals(registered, json) && registered != json)
                progress.Report(new StepProgress("Resolved ephemeral download URLs."));
        }
        Directory.CreateDirectory(ctx.ModlistDir);
        AtomicFile.WriteAllText(ctx.EmittedUmoListPath, registered);

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

        // Mod dirs only ever get CREATED during the run, so the after-run
        // pending set is a subset of this one: equal counts = zero progress.
        var pendingBefore = PendingMods(ctx).Count();

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

        // Disk truth is the authority on WHAT failed: any non-skipped mod
        // whose data dir never appeared. Parser events supply the WHY, but
        // umo's prose must never decide the failure list (it once produced a
        // state file full of mod-description fragments).
        var missing = PendingMods(ctx).DistinctBy(m => m.Name).ToList();
        ctx.State.FailedMods = missing.Select(m => m.Name).ToList();
        ctx.SaveState();

        if (missing.Count == 0)
        {
            if (!result.Process.Success)
                throw new InvalidOperationException(
                    $"umo install exited with code {result.Process.ExitCode}.");
            return;
        }

        // A wall, not N mods: the MAJORITY of the pending set failed. "Some
        // progress" must never veto this — the ~30 non-Nexus mods (github,
        // gitlab, direct) sail straight through a Nexus wall, so a dead key
        // or a full disk still shows progress. Field-hit twice: a single
        // 401 surfaced as "586 mods failed" with a skip-them-all button.
        // Small runs stay per-mod — a short list beats a verdict.
        var dominant = DominantError(result.ErrorLines);
        var majorityFailed = missing.Count * 2 >= pendingBefore;
        if (pendingBefore >= 5 && majorityFailed
            && (missing.Count == pendingBefore || missing.Count >= 25))
            throw new DownloaderFailedException(
                ExplainSystemic(dominant, result.Process, pendingBefore - missing.Count, missing.Count),
                dominant, missing.Count);

        throw new ModsFailedException(missing
            .Select(m => new FailedMod(m.Name, result.FailureReasons.GetValueOrDefault(m.Name)))
            .ToList());
    }

    /// <summary>The error line behind ≥80% of all reported errors, if any.</summary>
    private static string? DominantError(IReadOnlyList<string> errorLines)
    {
        if (errorLines.Count < 3)
            return null;
        var top = errorLines.GroupBy(e => e).MaxBy(g => g.Count())!;
        return top.Count() * 5 >= errorLines.Count * 4 ? top.Key : null;
    }

    private static string ExplainSystemic(string? dominant, IO.ProcessResult process, int arrived, int missing)
    {
        var tally = arrived > 0
            ? $"{arrived} mods made it; {missing} are waiting, and nothing already downloaded is ever re-fetched."
            : $"{missing} mods are waiting, and nothing already downloaded is ever re-fetched.";
        return dominant switch
        {
            { } d when d.Contains("401") =>
                $"Nexus rejected the sign-in (401). {tally} The API key is wrong, expired, or was " +
                "rotated — re-enter it and retry. Nothing needs skipping.",
            { } d when d.Contains("429") =>
                $"Nexus is rate-limiting this account (429). {tally} Wait a few minutes and retry. " +
                "Nothing needs skipping.",
            { } d when d.Contains("no space left", StringComparison.OrdinalIgnoreCase)
                       || d.Contains("disk full", StringComparison.OrdinalIgnoreCase) =>
                $"The disk filled up. {tally} The install needs room for the download cache AND the " +
                "extracted mods (~85 GB total) — free space, then retry. Nothing needs skipping.",
            { } d when d.Contains("connect", StringComparison.OrdinalIgnoreCase)
                       || d.Contains("resolve", StringComparison.OrdinalIgnoreCase)
                       || d.Contains("timed out", StringComparison.OrdinalIgnoreCase) =>
                $"The network connection dropped. {tally} Check connectivity and retry. " +
                $"Nothing needs skipping. ({d})",
            { } d =>
                $"Every failure shares one cause: {d} {tally} Fix that, then retry. Nothing needs skipping.",
            null when !process.Success =>
                $"The downloader died partway (exit code {process.ExitCode}). {tally} See the log " +
                "below for its last words, then retry. Nothing needs skipping.",
            _ =>
                $"The downloader finished without reporting errors, yet {missing} mod folders are not " +
                "where the installer expects them — that is a bug on our side, not in your setup. " +
                "Save the diagnostics zip below and send it in. Retrying is safe; nothing needs skipping.",
        };
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

    // Pre-generation is an optimization — OpenMW builds navmesh in the
    // background at runtime. An OOM-killed tool must not fail the install.
    public bool IsOptional => true;

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

/// <summary>
/// Downloads the mods umo's direct handler cannot: bare plugin files (umo
/// hard-rejects non-archives after download) and mediafire-hosted archives
/// (its downloader receives an HTML interstitial). Files land directly in
/// the mod's final dir BEFORE install-mods runs, so umo's own
/// dir-already-exists check skips them cleanly (field-verified behavior).
/// </summary>
public sealed class PreFetchUnsupportedDownloadsStep(
    HttpClient http,
    EphemeralUrlResolver resolver,
    ArchiveExtractor extractor,
    Func<InstallContext, string?> sevenZipExe) : IInstallStep
{
    public string Id => "prefetch-direct";
    public string Label => "Fetch downloads the downloader can't handle";

    private static readonly string[] PluginExts = [".esp", ".esm", ".omwaddon", ".omwscripts"];

    private static string? UrlPathExt(string url)
    {
        var path = url.Split('?')[0];
        return Path.GetExtension(path).ToLowerInvariant() is { Length: > 0 } e ? e : null;
    }

    private static bool NeedsPreFetch(string url) =>
        PluginExts.Contains(UrlPathExt(url)) ||
        url.StartsWith("https://www.mediafire.com/file/", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<(Modlist.ModEntry Mod, string Url, string Dir)> Targets(InstallContext ctx) =>
        from mod in ctx.Modlist.Mods
        let url = mod.Downloads.FirstOrDefault()?.DirectUrl
        where url is not null && NeedsPreFetch(url)
        let extractTo = mod.Downloads[0].ExtractTo ?? mod.Id
        select (mod, url, Path.Combine(
            ctx.ModsRootDir, ctx.Modlist.Name,
            Modlist.ModlistCompiler.CategoryDir(mod.Category), extractTo));

    public bool Verify(InstallContext ctx) => Targets(ctx).All(t =>
        Directory.Exists(t.Dir) && Directory.EnumerateFileSystemEntries(t.Dir).Any());

    public async Task RunAsync(InstallContext ctx, IProgress<StepProgress> progress, CancellationToken ct)
    {
        foreach (var (mod, url, dir) in Targets(ctx))
        {
            if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
                continue;

            var resolved = await resolver.ResolveUrlAsync(url, ct).ConfigureAwait(false);
            var fileName = Uri.UnescapeDataString(
                Path.GetFileName(resolved.Split('?')[0]).Replace("+", " "));
            progress.Report(new StepProgress($"fetching {mod.Id}: {fileName}"));

            var tmp = Path.Combine(ctx.DownloadCacheDir, "prefetch", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
            await using (var src = await http.GetStreamAsync(resolved, ct).ConfigureAwait(false))
            await using (var dst = File.Create(tmp))
                await src.CopyToAsync(dst, ct).ConfigureAwait(false);

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (PluginExts.Contains(ext))
            {
                var magic = new byte[4];
                await using (var check = File.OpenRead(tmp))
                    _ = await check.ReadAsync(magic, ct).ConfigureAwait(false);
                if (ext is not ".omwscripts" && "TES3"u8.ToArray() is var tes3 && !magic.SequenceEqual(tes3))
                    throw new InvalidOperationException(
                        $"{mod.Id}: downloaded '{fileName}' is not a TES3 plugin (got HTML error page?).");
                Directory.CreateDirectory(dir);
                File.Copy(tmp, Path.Combine(dir, fileName), overwrite: true);
            }
            else if (ext is ".zip")
            {
                extractor.ExtractZip(tmp, dir);
            }
            else
            {
                var sevenZip = sevenZipExe(ctx)
                    ?? throw new InvalidOperationException("7z tool not available for pre-fetch extraction.");
                await extractor.Extract7zAsync(sevenZip, tmp, dir, null, ct).ConfigureAwait(false);
            }
            progress.Report(new StepProgress($"installed {mod.Id} → {dir}"));
        }
    }
}

/// <summary>
/// Copies repo-shipped record-repair plugins (data/fixups) into
/// mods/mri-fixups. These override broken records in mods we must not edit
/// in place (re-extraction would revert the edit); the load-order plan
/// mounts them after all modlist content, before the delta merge.
/// </summary>
public sealed class InstallFixupsStep(string? fixupsSourceDir) : IInstallStep
{
    public string Id => "install-fixups";
    public string Label => "Install record-repair plugins";

    private const string PatchesFileName = "script-patches.json";

    private IReadOnlyList<string> SourceFiles =>
        fixupsSourceDir is not null && Directory.Exists(fixupsSourceDir)
            ? Directory.EnumerateFiles(fixupsSourceDir)
                .Where(f => !Path.GetFileName(f).Equals(PatchesFileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

    public sealed record ScriptPatch(string File, string Find, string Replace);

    private IReadOnlyList<ScriptPatch> Patches()
    {
        var path = fixupsSourceDir is null ? null : Path.Combine(fixupsSourceDir, PatchesFileName);
        if (path is null || !File.Exists(path))
            return [];
        return System.Text.Json.JsonSerializer.Deserialize<List<ScriptPatch>>(
            File.ReadAllText(path),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }

    /// <summary>Applied when the replacement text is present; mod re-extraction
    /// reverts the file, fails this check and triggers a clean re-apply.</summary>
    private static bool PatchApplied(InstallContext ctx, ScriptPatch p)
    {
        var target = Path.Combine(ctx.ModsRootDir, p.File);
        return File.Exists(target) && File.ReadAllText(target).Contains(p.Replace, StringComparison.Ordinal);
    }

    public bool Verify(InstallContext ctx) =>
        SourceFiles.All(src =>
        {
            var dest = Path.Combine(ctx.FixupsDir, Path.GetFileName(src));
            return File.Exists(dest) && new FileInfo(dest).Length == new FileInfo(src).Length;
        })
        && Patches().All(p => PatchApplied(ctx, p));

    public Task RunAsync(InstallContext ctx, IProgress<StepProgress> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(ctx.FixupsDir);
        foreach (var src in SourceFiles)
        {
            File.Copy(src, Path.Combine(ctx.FixupsDir, Path.GetFileName(src)), overwrite: true);
            progress.Report(new StepProgress($"fixup installed: {Path.GetFileName(src)}"));
        }

        foreach (var p in Patches())
        {
            if (PatchApplied(ctx, p))
                continue;
            var target = Path.Combine(ctx.ModsRootDir, p.File);
            if (!File.Exists(target))
                throw new InvalidOperationException(
                    $"script patch target missing: {p.File} (mod layout changed?)");
            var text = File.ReadAllText(target);
            var at = text.IndexOf(p.Find, StringComparison.Ordinal);
            if (at < 0)
                throw new InvalidOperationException(
                    $"script patch anchor not found in {p.File} (mod updated? re-verify the patch)");
            AtomicFile.WriteAllText(
                target, text[..at] + p.Replace + text[(at + p.Find.Length)..]);
            progress.Report(new StepProgress($"script patched: {p.File}"));
        }
        return Task.CompletedTask;
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
