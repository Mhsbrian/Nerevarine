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
