using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mri.Core.Modlist;

/// <summary>
/// Projects the canonical modlist into its two consumers: umo's ModDesc[] JSON
/// (what `umo list add` ingests — same schema modding-openmw.com serves from
/// /api/lists/&lt;slug&gt;) and the LoadOrderPlan the openmw.cfg composer uses.
/// One source, two projections, no drift.
/// </summary>
public static class ModlistCompiler
{
    /// <summary>
    /// Fixed timestamp for ModDesc date fields umo requires but we don't care
    /// about — keeps emitted JSON byte-stable across runs.
    /// </summary>
    private const string EpochDate = "2026-01-01T00:00:00+00:00";

    /// <summary>
    /// umo extracts every mod to BASEPATH/&lt;category&gt;/&lt;extract_to&gt; with the
    /// category string used VERBATIM as a directory (helper.where in umo
    /// source). Display categories like "Bug Fixes / Patches" would become
    /// mangled nested folders on Windows, so every path consumer — the umo
    /// projection, the openmw.cfg data plan, and the install verifier — goes
    /// through this one sanitizer and can never disagree.
    /// </summary>
    public static string CategoryDir(string category)
    {
        var token = string.Concat(category
            .Split(' ', '/', '\\', '-', '_', '(', ')', '\'', '.', ':', '&')
            .Where(w => w.Length > 0)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        if (token.Length == 0)
            token = "Uncategorized";
        // umo silently filters these category names out of custom lists.
        return token is "Settings" or "Tools" or "FirstSteps" ? token + "Mods" : token;
    }

    public static string ToUmoModDescJson(Modlist modlist)
    {
        var array = new JsonArray();
        foreach (var mod in modlist.Mods)
            array.Add(ToModDesc(mod, modlist.Name));

        return array.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject ToModDesc(ModEntry mod, string listName)
    {
        var downloadInfo = new JsonArray();
        foreach (var download in mod.Downloads)
        {
            downloadInfo.Add(new JsonObject
            {
                ["direct_download"] = download.DirectUrl,
                ["file_name"] = download.FileName,
                ["extract_to"] = download.ExtractTo,
                ["nexus_file_id"] = download.NexusFileId,
                ["pinned"] = download.Pinned && download.NexusFileId is not null,
                ["actions"] = ToActions(download.Actions),
            });
        }

        var plugins = mod.Content
            .Where(c => c.Mode != ContentMode.Disabled)
            .Select(c => c.File)
            .ToList();

        return new JsonObject
        {
            ["name"] = mod.Name,
            ["author"] = mod.Author ?? "",
            ["description"] = mod.Notes ?? mod.Name,
            ["url"] = mod.Source.Url,
            ["category"] = CategoryDir(mod.Category),
            ["dl_url"] = mod.Source.Handler == ModHandler.Nexus ? "" : mod.Downloads.FirstOrDefault()?.DirectUrl ?? "",
            ["usage_notes"] = "",
            ["compat"] = 4,
            ["dir"] = TopLevelDir(mod),
            ["slug"] = mod.Id,
            ["date_added"] = EpochDate,
            ["date_updated"] = mod.Provenance?.ResolvedAt ?? EpochDate,
            ["download_info"] = downloadInfo,
            ["tags"] = new JsonArray(mod.Tags.Select(t => (JsonNode)t).ToArray()),
            ["on_lists"] = new JsonArray(listName),
            ["data_paths"] = new JsonArray(mod.DataPaths.Select(p => (JsonNode)p).ToArray()),
            ["plugins"] = plugins.Count > 0
                ? new JsonArray(plugins.Select(p => (JsonNode)p).ToArray())
                : null,
            ["handler"] = mod.Source.Handler switch
            {
                ModHandler.Nexus => "nexus",
                ModHandler.Github => "github",
                _ => "direct",
            },
            // umo's ModDesc declares nexus_id as Optional[str] — an int fails
            // Pydantic validation (field-confirmed). umo re-derives it from the
            // url regex anyway; emitting it as a string keeps the data honest.
            ["nexus_id"] = mod.Source.NexusId?.ToString(),
            ["nexus_game"] = mod.Source.Handler == ModHandler.Nexus ? mod.Source.NexusGame : null,
        };
    }

    private static JsonArray ToActions(IReadOnlyList<FileAction> actions)
    {
        var array = new JsonArray();
        foreach (var action in actions)
        {
            var obj = action.Type switch
            {
                FileActionType.Remove when action.Paths is { Count: > 0 } => new JsonObject
                {
                    ["action"] = "remove",
                    ["paths"] = new JsonArray(action.Paths.Select(p => (JsonNode)p).ToArray()),
                },
                FileActionType.Remove => new JsonObject
                {
                    ["action"] = "remove",
                    ["path"] = Require(action.Path, action),
                },
                FileActionType.Rename => new JsonObject
                {
                    ["action"] = "rename",
                    ["src"] = Require(action.Src, action),
                    ["dst"] = Require(action.Dst, action),
                },
                FileActionType.Copy => new JsonObject
                {
                    ["action"] = "copy",
                    ["src"] = Require(action.Src, action),
                    ["dst"] = Require(action.Dst, action),
                    ["force"] = action.Force,
                },
                FileActionType.Clean => new JsonObject
                {
                    ["action"] = "clean",
                    ["path"] = Require(action.Path, action),
                    ["arguments"] = new JsonArray((action.Arguments ?? Array.Empty<string>())
                        .Select(a => (JsonNode)a).ToArray()),
                },
                _ => throw new InvalidDataException($"Unknown action type {action.Type}"),
            };
            array.Add(obj);
        }
        return array;
    }

    private static string Require(string? value, FileAction action) =>
        value ?? throw new InvalidDataException($"Action {action.Type} is missing a required field.");

    /// <summary>The mod's root folder under the mod base dir (umo's "dir").</summary>
    private static string TopLevelDir(ModEntry mod)
    {
        var first = mod.Downloads.FirstOrDefault()?.ExtractTo
            ?? mod.DataPaths.FirstOrDefault()
            ?? mod.Id;
        var separatorIndex = first.IndexOfAny(['/', '\\']);
        return separatorIndex < 0 ? first : first[..separatorIndex];
    }

    public static LoadOrderPlan BuildLoadOrderPlan(Modlist modlist, LoadOrderOptions options)
    {
        var dataDirs = new List<string>();
        var content = new List<string>();
        var groundcover = new List<string>();
        var archives = new List<string>();

        foreach (var mod in modlist.Mods)
        {
            if (options.SkippedModIds.Contains(mod.Id))
                continue;

            // Mirror umo's on-disk layout: during install umo rebases to
            // BASEPATH/<list name> (subcmds.py: Config.BASEPATH += MODLIST),
            // so a mod lives at <mods root>/<list>/<category>/<data path>.
            dataDirs.AddRange(mod.DataPaths.Select(p =>
                $"{modlist.Name}/{CategoryDir(mod.Category)}/{p}"));
            archives.AddRange(mod.BsaArchives);
            groundcover.AddRange(mod.Groundcover);

            foreach (var contentFile in mod.Content)
            {
                var include = contentFile.Mode switch
                {
                    ContentMode.Normal => true,
                    // deltaOnly plugins must be active while delta-plugin reads
                    // the cfg to merge them, then give way to the merged addon.
                    ContentMode.DeltaOnly => !options.IncludeDelta,
                    _ => false,
                };
                if (include)
                    content.Add(contentFile.File);
            }
        }

        if (options.IncludeDelta)
        {
            dataDirs.Add(options.DeltaDataDir);
            content.Add(options.DeltaContentFile);
        }

        return new LoadOrderPlan
        {
            DataDirs = dataDirs,
            ContentFiles = content,
            GroundcoverFiles = groundcover,
            FallbackArchives = archives,
        };
    }
}
