using System.Text;
using Mri.Core.Modlist;

namespace Mri.Curation.Inspect;

/// <summary>
/// Turns installed-but-dormant plugins into content/groundcover lists:
///
///   1. Groundcover-category mods route their .esp files to groundcover=
///      (never content= — grass plugins are a separate OpenMW mechanism).
///   2. Mods with a MOMW-curated plugins list adopt it (disk-gated).
///   3. Remaining mods activate every plugin found in their chosen data
///      paths — .esm/.omwgame first, then .esp/.omwaddon, then .omwscripts.
///   4. The whole plan is dependency-validated via TES3 MAST headers:
///      a plugin whose masters aren't all in the active set is dropped with
///      a report line (OpenMW hard-fails on missing masters), iterated to
///      a fixed point.
/// </summary>
public static class ActivationPlanner
{
    public sealed record PlanResult(
        string Yaml,
        int ModsActivated,
        int PluginsActivated,
        int Groundcover,
        IReadOnlyList<string> Dropped);

    public static PlanResult Plan(
        string modsRoot,
        Modlist modlist,
        IReadOnlyDictionary<string, MomwAdopter.MomwEntry> momw)
    {
        // mod → ordered plugin file names (existing on disk), plus scan of masters.
        var perMod = new Dictionary<string, List<string>>();      // content candidates
        var perModGroundcover = new Dictionary<string, List<string>>();
        var masters = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in modlist.Mods)
        {
            var categoryDir = ModlistCompiler.CategoryDir(mod.Category);
            var isGroundcover = categoryDir.Equals("Groundcover", StringComparison.OrdinalIgnoreCase);

            var found = new List<(string File, string FullPath)>();
            foreach (var dataPath in mod.DataPaths)
            {
                var root = Path.Combine(modsRoot, categoryDir, dataPath);
                foreach (var plugin in PluginScanner.FindPlugins(root))
                    found.Add((Path.GetFileName(plugin), plugin));
            }
            if (found.Count == 0)
                continue;

            foreach (var (file, full) in found)
                if (!masters.ContainsKey(file) &&
                    Path.GetExtension(file).ToLowerInvariant() is ".esm" or ".esp" or ".omwaddon" or ".omwgame")
                    masters[file] = PluginScanner.TryRead(full)?.Masters.ToList() ?? [];

            if (isGroundcover)
            {
                perModGroundcover[mod.Id] = found.Select(f => f.File).ToList();
                continue;
            }

            // MOMW's curated plugins list wins where present and disk-complete.
            List<string> chosen;
            if (mod.Source.NexusId is { } nexusId &&
                momw.TryGetValue(nexusId.ToString(), out var entry) &&
                entry.Plugins.Count > 0 &&
                entry.Plugins.All(p => found.Any(f => f.File.Equals(p, StringComparison.OrdinalIgnoreCase))))
            {
                chosen = entry.Plugins
                    .Select(p => found.First(f => f.File.Equals(p, StringComparison.OrdinalIgnoreCase)).File)
                    .ToList();
            }
            else
            {
                chosen = found.Select(f => f.File)
                    .OrderBy(f => Path.GetExtension(f).ToLowerInvariant() switch
                    {
                        ".omwgame" => 0, ".esm" => 1, ".esp" => 2, ".omwaddon" => 3, _ => 4,
                    })
                    .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            perMod[mod.Id] = chosen;
        }

        // Dependency fixed point: active set = vanilla + everything planned;
        // drop plugins with unsatisfied masters until stable.
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Morrowind.esm", "Tribunal.esm", "Bloodmoon.esm",
        };
        foreach (var list in perMod.Values)
            foreach (var plugin in list)
                active.Add(plugin);

        var dropped = new List<string>();
        bool changed;
        do
        {
            changed = false;
            foreach (var (modId, list) in perMod)
            {
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    var plugin = list[i];
                    if (!masters.TryGetValue(plugin, out var deps))
                        continue; // omwscripts etc. have no masters
                    var missing = deps.Where(d => !active.Contains(d)).ToList();
                    if (missing.Count > 0)
                    {
                        dropped.Add($"{modId}: '{plugin}' needs missing master(s): {string.Join(", ", missing)}");
                        active.Remove(plugin);
                        list.RemoveAt(i);
                        changed = true;
                    }
                }
            }
        } while (changed);

        // Mod-level master ordering: a patch mod whose plugin needs a master
        // provided by a LATER mod must move after that provider (field crash:
        // a TR dialogue patch loading ~200 slots before Tamriel Rebuilt
        // OOM-killed the engine). Emitted as moveAfter rules.
        var modIndex = modlist.Mods.Select((m, i) => (m.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var providerOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in modlist.Mods)
            if (perMod.TryGetValue(mod.Id, out var list))
                foreach (var plugin in list)
                    providerOf.TryAdd(plugin, modIndex[mod.Id]);

        var moveAfter = new Dictionary<string, string>();
        foreach (var mod in modlist.Mods)
        {
            if (!perMod.TryGetValue(mod.Id, out var list))
                continue;
            var mi = modIndex[mod.Id];
            int latestProvider = -1;
            foreach (var plugin in list)
                if (masters.TryGetValue(plugin, out var deps))
                    foreach (var dep in deps)
                        if (providerOf.TryGetValue(dep, out var pi) && pi > mi && pi > latestProvider)
                            latestProvider = pi;
            if (latestProvider >= 0)
                moveAfter[mod.Id] = modlist.Mods[latestProvider].Id;
        }

        // Emit YAML.
        var yaml = new StringBuilder();
        yaml.AppendLine("# GENERATED by `mri-curation activate` — plugin activation derived from");
        yaml.AppendLine("# disk scans + MOMW curated plugin lists, dependency-validated via TES3");
        yaml.AppendLine("# masters. Hand overrides.yaml is applied after and wins.");
        yaml.AppendLine("version: 1");
        yaml.AppendLine("rules:");

        var totalPlugins = 0;
        foreach (var mod in modlist.Mods)
        {
            if (perModGroundcover.TryGetValue(mod.Id, out var grass) && grass.Count > 0)
            {
                yaml.AppendLine($"  - match: {{ slug: {Q(mod.Id)} }}");
                yaml.AppendLine("    set:");
                yaml.AppendLine($"      groundcover: [{string.Join(", ", grass.Select(Q))}]");
                continue;
            }

            if (!perMod.TryGetValue(mod.Id, out var plugins) || plugins.Count == 0)
                continue;
            totalPlugins += plugins.Count;
            yaml.AppendLine($"  - match: {{ slug: {Q(mod.Id)} }}");
            yaml.AppendLine("    set:");
            yaml.AppendLine("      content:");
            foreach (var plugin in plugins)
                yaml.AppendLine($"        - {{ file: {Q(plugin)}, mode: \"normal\" }}");
        }

        foreach (var (modId, anchor) in moveAfter)
        {
            yaml.AppendLine($"  - match: {{ slug: {Q(modId)} }}   # master provided by a later mod");
            yaml.AppendLine($"    moveAfter: {Q(anchor)}");
        }

        foreach (var drop in dropped.Distinct())
            yaml.AppendLine($"  # DROPPED {drop}");

        return new PlanResult(
            yaml.ToString(),
            perMod.Count(kv => kv.Value.Count > 0),
            totalPlugins,
            perModGroundcover.Sum(kv => kv.Value.Count),
            dropped.Distinct().ToList());
    }

    private static string Q(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
