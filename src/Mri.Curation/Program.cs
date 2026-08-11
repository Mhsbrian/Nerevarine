using System.Text.Json;
using Mri.Core.Modlist;
using Mri.Curation.Emit;
using Mri.Curation.Overrides;
using Mri.Curation.Parsing;
using Mri.Curation.Resolve;

namespace Mri.Curation;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            return args switch
            {
                ["parse", .. var rest] => Parse(Options.From(rest)),
                ["resolve", .. var rest] => await ResolveAsync(Options.From(rest)),
                ["emit", .. var rest] => Emit(Options.From(rest)),
                ["check", .. var rest] => Check(Options.From(rest)),
                ["tools-check", .. var rest] => await ToolsCheckAsync(Options.From(rest)),
                ["install", .. var rest] => await InstallAsync(Options.From(rest)),
                _ => Usage(),
            };
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("""
            mri-curation — spreadsheet → canonical modlist pipeline

            verbs:
              parse    --csv <file> [--out build/parsed.json]
                       classify rows, extract nexus ids, generate slugs
              resolve  --csv <file> [--cache data/resolve-cache.json] [--limit N]
                       enrich nexus mods via the API (needs NEXUS_APIKEY env var)
              emit     --csv <file> [--cache …] [--overrides data/overrides/overrides.yaml]
                       [--out data/modlist.json] [--report build/report.md]
                       [--list-version YYYY.MM.P] [--strict]
                       produce the canonical modlist + curation report
              check    same inputs as emit; exit 1 on any constraint violation
                       (CI gate — does not write outputs)
              tools-check  [--dir <dir>]  (default build/tools-check)
                       REAL tool acquisition for this platform: download,
                       extract, probe, run umo/iniimporter, detect Steam.
                       The platform-parity smoke test.
            """);
        return 2;
    }

    private sealed record Options(Dictionary<string, string> Values)
    {
        public static Options From(string[] args)
        {
            var values = new Dictionary<string, string>();
            for (var i = 0; i < args.Length - 1; i += 2)
            {
                if (!args[i].StartsWith("--"))
                    throw new ArgumentException($"Expected an --option, got '{args[i]}'.");
                values[args[i][2..]] = args[i + 1];
            }
            return new Options(values);
        }

        public string Csv => Require("csv");
        public string Cache => Values.GetValueOrDefault("cache", "data/resolve-cache.json");
        public string Overrides => Values.GetValueOrDefault("overrides", "data/overrides/overrides.yaml");
        public string Out(string fallback) => Values.GetValueOrDefault("out", fallback);
        public string Report => Values.GetValueOrDefault("report", "build/report.md");
        public string UmoOut => Values.GetValueOrDefault("umo-out", "build/umo-list.json");
        public string ListVersion => Values.GetValueOrDefault("list-version",
            $"{DateTime.UtcNow:yyyy.MM}.0");
        public int? Limit => Values.TryGetValue("limit", out var limit) ? int.Parse(limit) : null;
        public bool Strict => Values.ContainsKey("strict");

        private string Require(string key) =>
            Values.GetValueOrDefault(key)
            ?? throw new ArgumentException($"--{key} is required.");
    }

    private static int Parse(Options options)
    {
        var rows = RowParser.Parse(File.ReadAllText(options.Csv));
        var outPath = options.Out("build/parsed.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, JsonSerializer.Serialize(rows, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        }));

        var mods = rows.Count(r => r.Kind == RowKind.Mod);
        Console.WriteLine($"{rows.Count} rows: {mods} mods, " +
                          $"{rows.Count(r => r.Kind == RowKind.CategoryHeader)} categories, " +
                          $"{rows.Count(r => r.Kind == RowKind.Note)} notes → {outPath}");
        Console.WriteLine($"  nexus: {rows.Count(r => r is { Kind: RowKind.Mod, Handler: "nexus" })}" +
                          $" (with file_id: {rows.Count(r => r is { Kind: RowKind.Mod, NexusFileId: not null })})" +
                          $", github/gitlab: {rows.Count(r => r is { Kind: RowKind.Mod, Handler: "github" })}" +
                          $", direct: {rows.Count(r => r is { Kind: RowKind.Mod, Handler: "direct" })}");
        return 0;
    }

    private static async Task<int> ResolveAsync(Options options)
    {
        var apiKey = Environment.GetEnvironmentVariable("NEXUS_APIKEY")
            ?? throw new InvalidOperationException("Set the NEXUS_APIKEY environment variable first.");

        var rows = RowParser.Parse(File.ReadAllText(options.Csv));
        var cache = ResolveCache.LoadFile(options.Cache);
        var resolver = new NexusResolver(new HttpClient(), apiKey, Console.WriteLine);

        var pending = rows
            .Where(r => r is { Kind: RowKind.Mod, NexusId: not null })
            .Select(r => r.NexusId!.Value)
            .Distinct()
            .Where(id => cache.Get(id) is null)
            .Take(options.Limit ?? int.MaxValue)
            .ToList();

        Console.WriteLine($"{pending.Count} mods to resolve (cache already has {cache.Mods.Count}).");
        var done = 0;
        try
        {
            foreach (var nexusId in pending)
            {
                cache.Put(nexusId, await resolver.ResolveAsync(nexusId));
                if (++done % 10 == 0)
                {
                    cache.SaveFile(options.Cache);
                    Console.WriteLine($"  {done}/{pending.Count} (cache checkpointed)");
                }
            }
        }
        finally
        {
            cache.SaveFile(options.Cache);
        }

        Console.WriteLine($"Resolved {done}; cache now covers {cache.Mods.Count} mods → {options.Cache}");
        return 0;
    }

    private static int Emit(Options options)
    {
        var result = RunEmit(options);

        var outPath = options.Out("data/modlist.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, ModlistLoader.Serialize(result.Modlist));

        // The umo ModDesc projection — what `umo list add` will actually
        // ingest. tools/validate-moddesc.py runs umo's own Pydantic model
        // against this file so contract violations surface here, not mid-install.
        var umoOut = options.UmoOut;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(umoOut))!);
        File.WriteAllText(umoOut, ModlistCompiler.ToUmoModDescJson(result.Modlist));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.Report))!);
        File.WriteAllText(options.Report, result.Report);

        Console.WriteLine($"{result.Modlist.Mods.Count} mods → {outPath}");
        Console.WriteLine($"{result.ProblemCount} open problems, " +
                          $"{result.ConstraintViolations.Count} constraint violations → {options.Report}");

        if (result.ConstraintViolations.Count > 0)
        {
            Console.Error.WriteLine("Constraint violations are build-blocking:");
            foreach (var violation in result.ConstraintViolations)
                Console.Error.WriteLine($"  ❌ {violation}");
            return 1;
        }
        return options.Strict && result.ProblemCount > 0 ? 1 : 0;
    }

    private static int Check(Options options)
    {
        var result = RunEmit(options);
        Console.WriteLine($"{result.Modlist.Mods.Count} mods, {result.ProblemCount} open problems, " +
                          $"{result.ConstraintViolations.Count} violations");
        foreach (var violation in result.ConstraintViolations)
            Console.Error.WriteLine($"  ❌ {violation}");
        return result.ConstraintViolations.Count > 0 ? 1 : 0;
    }

    /// <summary>
    /// Headless installer: drives the EXACT production pipeline
    /// (PipelineFactory — same steps, same logging) without the wizard UI.
    /// The dev/tester harness: --list &lt;modlist.json&gt; --dir &lt;install dir&gt;
    /// [--game &lt;path&gt;] [--skip step-id,step-id] [--key or NEXUS_APIKEY env].
    /// </summary>
    private static async Task<int> InstallAsync(Options options)
    {
        var installDir = Path.GetFullPath(options.Values.GetValueOrDefault("dir")
            ?? throw new ArgumentException("--dir is required."));
        var listPath = options.Values.GetValueOrDefault("list", "data/modlist.json");
        var skips = (options.Values.GetValueOrDefault("skip") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet();
        var apiKey = options.Values.GetValueOrDefault("key")
            ?? Environment.GetEnvironmentVariable("NEXUS_APIKEY") ?? "";

        // Game: explicit --game or first auto-detected candidate.
        Mri.Core.GameDetection.GameValidation game;
        if (options.Values.GetValueOrDefault("game") is { } gamePath)
        {
            game = Mri.Core.GameDetection.GameValidator.Validate(gamePath);
        }
        else
        {
            var registry = OperatingSystem.IsWindows()
                ? (Mri.Core.GameDetection.IRegistryReader)new Mri.Core.GameDetection.WindowsRegistryReader()
                : new Mri.Core.GameDetection.NullRegistryReader();
            var candidate = new Mri.Core.GameDetection.GamePathService(registry).DetectCandidates().FirstOrDefault()
                ?? throw new InvalidOperationException("No Morrowind install detected — pass --game <path>.");
            Console.WriteLine($"game auto-detected ({candidate.Source}): {candidate.Path}");
            game = candidate.Validation;
        }
        if (!game.IsValid)
            throw new InvalidOperationException($"Game validation failed: {game.FailReason}");

        Directory.CreateDirectory(installDir);
        var modlist = ModlistLoader.LoadFile(listPath);
        var stateStore = new Mri.Core.Pipeline.InstallStateStore(Path.Combine(installDir, "state.json"));
        var ctx = new Mri.Core.Pipeline.InstallContext
        {
            InstallDir = installDir,
            Game = game,
            Modlist = modlist,
            ToolManifest = Mri.Core.Tools.ToolManifest.Load(File.ReadAllText("data/tools.json")),
            NexusApiKey = apiKey,
            OpenMwPaths = Mri.Core.OpenMw.OpenMwUserPaths.Detect(),
            State = stateStore.Load(),
            StateStore = stateStore,
        };

        using var log = Mri.Core.Logging.InstallLog.CreateInDirectory(
            Path.Combine(installDir, "logs"), "dev-cli");
        log.AddRedaction(apiKey);
        Console.WriteLine($"log: {log.FilePath}");

        var engine = Mri.Core.Pipeline.PipelineFactory.Create(
            ctx,
            new HttpClient(),
            new Mri.Core.Logging.LoggingProcessRunner(new Mri.Core.IO.ProcessRunner(), log),
            File.ReadAllText("data/templates/settings.template.cfg"),
            File.ReadAllText("data/templates/shaders.template.yaml"),
            log);

        var steps = engine.Steps.Where(s => !skips.Contains(s.Id)).ToList();
        if (skips.Count > 0)
            Console.WriteLine($"skipping steps: {string.Join(", ", skips)}");

        var result = await new Mri.Core.Pipeline.InstallEngine(steps, log).RunAsync(
            ctx,
            new Progress<Mri.Core.Pipeline.EngineProgress>(p =>
            {
                if (p.Detail is null || p.Status != Mri.Core.Pipeline.StepStatus.Running)
                    Console.WriteLine($"[{p.StepId}] {p.Status}{(p.Detail is { } d ? ": " + d.Message : "")}");
            }));

        Console.WriteLine(result.Success
            ? "INSTALL SUCCEEDED"
            : $"INSTALL FAILED at {result.FailedStepId}: {result.Error?.Message}");
        return result.Success ? 0 : 1;
    }

    /// <summary>
    /// The platform-parity smoke: acquires the REAL tools for this OS into a
    /// scratch dir, probes every binary the pipeline needs, and executes umo
    /// and openmw-iniimporter to prove they actually run here.
    /// </summary>
    private static async Task<int> ToolsCheckAsync(Options options)
    {
        var dir = Path.GetFullPath(options.Values.GetValueOrDefault("dir", "build/tools-check"));
        Console.WriteLine($"platform: {Mri.Core.Tools.ToolManifest.CurrentRid}; tools dir: {dir}");

        var manifestPath = "data/tools.json";
        var manifest = Mri.Core.Tools.ToolManifest.Load(File.ReadAllText(manifestPath));
        var runner = new Mri.Core.IO.ProcessRunner();
        var tools = new Mri.Core.Tools.ToolAcquisitionService(new HttpClient(), runner, dir);

        var lastPercent = -1;
        await tools.EnsureAllAsync(manifest, new Progress<Mri.Core.Tools.ToolProgress>(p =>
        {
            var percent = p.BytesTotal is > 0 ? (int)(100.0 * p.BytesDone / p.BytesTotal.Value) : -1;
            if (percent != lastPercent)
            {
                lastPercent = percent;
                Console.WriteLine($"  {p.ToolId}: {p.Phase} {(percent >= 0 ? percent + "%" : "")}");
            }
        }));

        var locator = new Mri.Core.Tools.ToolLocator(tools, manifest);
        var ok = true;
        foreach (var (name, path) in new (string, string?)[]
        {
            ("umo", locator.UmoExe),
            ("openmw", locator.OpenMwExe),
            ("openmw-iniimporter", locator.IniImporterExe),
            ("openmw-navmeshtool", locator.NavmeshToolExe),
            ("delta_plugin", locator.DeltaPluginExe),
            ("tes3cmd", locator.Tes3cmdExe),
            ("7z", locator.SevenZipExe),
            ("openmw-validator", locator.ValidatorExe),
        })
        {
            Console.WriteLine($"  probe {name,-20} {(path is null ? "✗ MISSING" : "✓ " + path)}");
            ok &= path is not null;
        }

        async Task<bool> RunsAsync(string label, string? exe, params string[] args)
        {
            if (exe is null)
                return false;
            var lines = new List<string>();
            try
            {
                var result = await runner.RunAsync(new Mri.Core.IO.ProcessSpec
                {
                    Exe = exe,
                    Args = args,
                    Env = new Dictionary<string, string> { ["UMO_CONF_DIR"] = Path.Combine(dir, "umo-conf") },
                    Timeout = TimeSpan.FromSeconds(60),
                }, new Progress<Mri.Core.IO.OutputLine>(l =>
                {
                    lock (lines)
                        lines.Add(l.Text);
                }));
                await Task.Delay(150);
                string first;
                lock (lines)
                    first = lines.FirstOrDefault("") ?? "";
                Console.WriteLine($"  run   {label,-20} exit {result.ExitCode}: {first[..Math.Min(first.Length, 60)]}");
                return result.Success;
            }
            catch (Exception e)
            {
                Console.WriteLine($"  run   {label,-20} ✗ {e.Message}");
                return false;
            }
        }

        ok &= await RunsAsync("umo --version", locator.UmoExe, "--version");
        ok &= await RunsAsync("iniimporter --help", locator.IniImporterExe, "--help");

        var registry = OperatingSystem.IsWindows()
            ? (Mri.Core.GameDetection.IRegistryReader)new Mri.Core.GameDetection.WindowsRegistryReader()
            : new Mri.Core.GameDetection.NullRegistryReader();
        var candidates = new Mri.Core.GameDetection.GamePathService(registry).DetectCandidates();
        Console.WriteLine(candidates.Count == 0
            ? "  steam: no Morrowind install detected (fine if not installed yet)"
            : $"  steam: found {string.Join("; ", candidates.Select(c => $"{c.Source}: {c.Path}"))}");

        Console.WriteLine(ok ? "TOOLS CHECK PASSED" : "TOOLS CHECK FAILED");
        return ok ? 0 : 1;
    }

    private static EmitResult RunEmit(Options options)
    {
        var rows = RowParser.Parse(File.ReadAllText(options.Csv));
        var cache = ResolveCache.LoadFile(options.Cache);
        var overrides = File.Exists(options.Overrides)
            ? OverridesFile.Load(File.ReadAllText(options.Overrides))
            : new OverridesFile();
        return ModlistEmitter.Emit(rows, cache, overrides, options.ListVersion);
    }
}
