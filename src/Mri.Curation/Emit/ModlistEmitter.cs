using System.Text;
using System.Text.RegularExpressions;
using Mri.Core.Modlist;
using Mri.Curation.Overrides;
using Mri.Curation.Parsing;
using Mri.Curation.Resolve;

namespace Mri.Curation.Emit;

public sealed record EmitResult(
    Mri.Core.Modlist.Modlist Modlist,
    string Report,
    int ProblemCount,
    IReadOnlyList<string> ConstraintViolations);

/// <summary>
/// parsed rows + resolve cache + overrides → canonical modlist + review report.
/// The report is the curation burn-down list: it names every row that still
/// needs human attention and every prose comment not yet covered by a rule.
/// </summary>
public static partial class ModlistEmitter
{
    [GeneratedRegex(@"[^A-Za-z0-9]")]
    private static partial Regex DirCleanRegex();

    public static EmitResult Emit(
        IReadOnlyList<ParsedRow> rows,
        ResolveCache cache,
        OverridesFile overrides,
        string listVersion)
    {
        var drafts = BuildDrafts(rows, cache);
        var matchedRules = ApplyOverrides(drafts, overrides);
        ApplyMoves(drafts, overrides);

        var active = drafts.Where(d => !d.Skipped).ToList();
        var modlist = new Mri.Core.Modlist.Modlist
        {
            ListVersion = listVersion,
            Name = "morrowind-remake",
            EstimatedDownloadBytes = active.Sum(d => d.SizeBytes),
            // Rough installed-size heuristic: archives roughly triple when unpacked.
            EstimatedInstalledBytes = active.Sum(d => d.SizeBytes) * 3,
            Mods = active.Select(d => d.ToModEntry()).ToList(),
        };

        var violations = CheckConstraints(active, modlist);
        var report = BuildReport(drafts, matchedRules, violations, listVersion);
        var problemCount = drafts.Where(d => !d.Skipped).Sum(d => d.Problems.Count);

        return new EmitResult(modlist, report, problemCount, violations);
    }

    private static List<DraftMod> BuildDrafts(IReadOnlyList<ParsedRow> rows, ResolveCache cache)
    {
        var drafts = new List<DraftMod>();
        foreach (var row in rows.Where(r => r.Kind == RowKind.Mod))
        {
            var dir = DirNameFor(row.Name);
            var draft = new DraftMod
            {
                Id = row.Slug,
                Name = row.Name,
                Category = row.Category,
                Handler = row.Handler ?? "direct",
                Url = row.PageLink!,
                NexusId = row.NexusId,
                NexusFileId = row.NexusFileId,
                ExtractTo = dir,
                DataPaths = [dir],
                Notes = row.Comment,
                Version = row.Version,
                CsvRow = row.CsvRow,
                FileName = row.Name,
            };

            if (draft.Handler == "direct")
                draft.DirectUrl = RewriteDirectUrl(row.PageLink!);

            EnrichFromCache(draft, cache);

            if (draft.Handler == "nexus" && draft.NexusId is null)
                draft.Problems.Add("nexus URL but no mod id could be extracted");
            if (draft.Handler == "nexus" && draft.NexusFileId is null && draft.NexusId is not null &&
                cache.Get(draft.NexusId.Value) is null)
                draft.Problems.Add("no file_id pinned and mod not yet in resolve cache (run `resolve`)");
            if (draft.Handler == "github")
                draft.Problems.Add("github/gitlab source — verify the URL is a direct artifact download");
            if (draft.Handler == "direct" && draft.DirectUrl is null)
                draft.Problems.Add("direct source with unusable URL");
            if (row.Comment is not null)
                draft.Problems.Add($"comment not encoded: \"{Truncate(row.Comment, 90)}\"");
            if (draft.Content.Count == 0)
                draft.Problems.Add("plugin list unknown (needs archive inspection or override)");

            drafts.Add(draft);
        }
        return drafts;
    }

    private static void EnrichFromCache(DraftMod draft, ResolveCache cache)
    {
        if (draft.NexusId is not { } nexusId || cache.Get(nexusId) is not { } cached)
            return;

        draft.Author = cached.Author;
        draft.ResolvedAt = cached.FetchedAt;
        if (!cached.Available)
        {
            draft.Problems.Add("mod is hidden/removed on Nexus — needs a mirror or removal");
            return;
        }

        if (draft.NexusFileId is { } pinned)
        {
            var file = cached.Files.FirstOrDefault(f => f.FileId == pinned);
            if (file is null)
            {
                draft.Problems.Add($"pinned file_id {pinned} not found on Nexus (typo in sheet?)");
                draft.NexusFileId = null;
            }
            else
            {
                ApplyPick(draft, file);
                return;
            }
        }

        var (best, confident) = FilePickHeuristics.Choose(cached.Files, draft.Name, draft.Version);
        if (best is null)
        {
            draft.Problems.Add("no downloadable files listed on Nexus");
            return;
        }
        ApplyPick(draft, best);
        if (!confident)
            draft.Problems.Add(
                $"low-confidence file pick '{best.Name}' ({best.FileId}) — confirm or pin via override");
    }

    private static void ApplyPick(DraftMod draft, CachedFile file)
    {
        draft.NexusFileId = file.FileId;
        draft.FileName = file.FileName ?? file.Name;
        draft.SizeBytes = file.SizeBytes;
        draft.ResolvedVersion = file.Version;
    }

    private static HashSet<OverrideRule> ApplyOverrides(List<DraftMod> drafts, OverridesFile overrides)
    {
        var matched = new HashSet<OverrideRule>();
        foreach (var rule in overrides.Rules)
        {
            var targets = drafts.Where(d => Matches(rule.Match, d)).ToList();
            if (targets.Count == 0)
                continue;
            matched.Add(rule);

            foreach (var draft in targets)
            {
                // A matching rule means a human looked at this row; the
                // "comment not encoded" nag is resolved.
                draft.Problems.RemoveAll(p => p.StartsWith("comment not encoded"));

                if (rule.Skip)
                {
                    draft.Skipped = true;
                    draft.SkipReason = rule.SkipReason;
                    continue;
                }

                if (rule.Set is { } set)
                    ApplySet(draft, set);
                if (rule.Actions is { } actions)
                    draft.Actions.AddRange(actions);
                if (rule.Constraints is { } constraints)
                    draft.Constraints.AddRange(constraints);
                if (rule.PickFile is not null)
                    draft.Problems.RemoveAll(p => p.StartsWith("low-confidence file pick"));

                if (rule.SplitInto is { Count: > 0 } splits)
                {
                    var index = drafts.IndexOf(draft);
                    drafts.RemoveAt(index);
                    foreach (var (split, offset) in splits.Select((s, n) => (s, n)))
                    {
                        var clone = CloneFor(draft, split, offset);
                        drafts.Insert(index + offset, clone);
                    }
                }
            }
        }
        return matched;
    }

    private static void ApplySet(DraftMod draft, SetSpec set)
    {
        if (set.Id is not null) draft.Id = set.Id;
        if (set.Name is not null) draft.Name = set.Name;
        if (set.ExtractTo is not null) draft.ExtractTo = set.ExtractTo;
        if (set.DirectUrl is not null) draft.DirectUrl = set.DirectUrl;
        if (set.NexusFileId is not null) draft.NexusFileId = set.NexusFileId;
        if (set.DataPaths is not null) draft.DataPaths = set.DataPaths.ToList();
        if (set.Groundcover is not null) draft.Groundcover = set.Groundcover.ToList();
        if (set.BsaArchives is not null) draft.BsaArchives = set.BsaArchives.ToList();
        if (set.Tags is not null) draft.Tags = set.Tags.ToList();
        if (set.Content is not null)
        {
            draft.Content = set.Content
                .Select(c => (c.File, DraftMod.ParseMode(c.Mode)))
                .ToList();
            draft.Problems.RemoveAll(p => p.StartsWith("plugin list unknown"));
        }
    }

    private static DraftMod CloneFor(DraftMod source, SetSpec split, int offset)
    {
        var clone = new DraftMod
        {
            Id = split.Id ?? $"{source.Id}-{offset + 1}",
            Name = split.Name ?? source.Name,
            Category = source.Category,
            Author = source.Author,
            Handler = source.Handler,
            Url = source.Url,
            NexusId = source.NexusId,
            NexusFileId = source.NexusFileId,
            DirectUrl = source.DirectUrl,
            FileName = source.FileName,
            SizeBytes = source.SizeBytes,
            ExtractTo = source.ExtractTo,
            Notes = source.Notes,
            Version = source.Version,
            CsvRow = source.CsvRow,
            ResolvedVersion = source.ResolvedVersion,
            ResolvedAt = source.ResolvedAt,
            DataPaths = source.DataPaths.ToList(),
        };
        ApplySet(clone, split);
        return clone;
    }

    private static void ApplyMoves(List<DraftMod> drafts, OverridesFile overrides)
    {
        foreach (var rule in overrides.Rules)
        {
            if (rule.MoveAfter is null && rule.MoveBefore is null)
                continue;

            var target = drafts.FirstOrDefault(d => Matches(rule.Match, d));
            if (target is null)
                continue;

            var anchorSlug = rule.MoveAfter ?? rule.MoveBefore!;
            var anchor = drafts.FirstOrDefault(d => d.Id == anchorSlug);
            if (anchor is null)
            {
                target.Problems.Add($"move anchor '{anchorSlug}' not found");
                continue;
            }

            drafts.Remove(target);
            var anchorIndex = drafts.IndexOf(anchor);
            drafts.Insert(rule.MoveAfter is not null ? anchorIndex + 1 : anchorIndex, target);
        }
    }

    private static bool Matches(MatchSpec match, DraftMod draft)
    {
        var hasCriterion = match.Slug is not null || match.NexusId is not null ||
                           match.CsvRow is not null || match.Name is not null;
        if (!hasCriterion)
            return false; // An empty matcher must never match everything.

        return (match.Slug is null || match.Slug == draft.Id) &&
               (match.NexusId is null || match.NexusId == draft.NexusId) &&
               (match.CsvRow is null || match.CsvRow == draft.CsvRow) &&
               (match.Name is null ||
                string.Equals(match.Name, draft.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> CheckConstraints(
        List<DraftMod> active, Mri.Core.Modlist.Modlist modlist)
    {
        var violations = new List<string>();
        var order = ModlistCompiler
            .BuildLoadOrderPlan(modlist, new LoadOrderOptions())
            .ContentFiles
            .Select((file, index) => (file, index))
            .ToDictionary(x => x.file, x => x.index, StringComparer.OrdinalIgnoreCase);

        foreach (var draft in active)
        foreach (var constraint in draft.Constraints)
        foreach (var (own, _) in draft.Content)
        {
            if (!order.TryGetValue(own, out var ownIndex))
                continue;

            if (constraint.ContentBefore is { } before &&
                order.TryGetValue(before, out var beforeIndex) && ownIndex >= beforeIndex)
                violations.Add($"{draft.Id}: '{own}' must load before '{before}'");

            if (constraint.ContentAfter is { } after &&
                order.TryGetValue(after, out var afterIndex) && ownIndex <= afterIndex)
                violations.Add($"{draft.Id}: '{own}' must load after '{after}'");
        }
        return violations;
    }

    private static string BuildReport(
        List<DraftMod> drafts,
        HashSet<OverrideRule> matchedRules,
        IReadOnlyList<string> violations,
        string listVersion)
    {
        var active = drafts.Where(d => !d.Skipped).ToList();
        var withProblems = active.Where(d => d.Problems.Count > 0).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# Curation report — list {listVersion}");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Count |");
        sb.AppendLine($"|---|---|");
        sb.AppendLine($"| Mods emitted | {active.Count} |");
        sb.AppendLine($"| Skipped rows | {drafts.Count(d => d.Skipped)} |");
        sb.AppendLine($"| Mods with open problems | {withProblems.Count} |");
        sb.AppendLine($"| Total open problems | {withProblems.Sum(d => d.Problems.Count)} |");
        sb.AppendLine($"| Constraint violations | {violations.Count} |");
        sb.AppendLine();

        if (violations.Count > 0)
        {
            sb.AppendLine("## ❌ Constraint violations (build-blocking)");
            foreach (var violation in violations)
                sb.AppendLine($"- {violation}");
            sb.AppendLine();
        }

        if (withProblems.Count > 0)
        {
            sb.AppendLine("## Open problems by category");
            foreach (var group in withProblems.GroupBy(d => d.Category))
            {
                sb.AppendLine($"### {group.Key}");
                foreach (var draft in group)
                {
                    sb.AppendLine($"- **{draft.Name}** (`{draft.Id}`, csv row {draft.CsvRow})");
                    foreach (var problem in draft.Problems)
                        sb.AppendLine($"  - {problem}");
                }
                sb.AppendLine();
            }
        }

        var skipped = drafts.Where(d => d.Skipped).ToList();
        if (skipped.Count > 0)
        {
            sb.AppendLine("## Skipped rows");
            foreach (var draft in skipped)
                sb.AppendLine($"- {draft.Name} — {draft.SkipReason ?? "no reason recorded"}");
        }

        return sb.ToString();
    }

    public static string DirNameFor(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => DirCleanRegex().Replace(w, ""))
            .Where(w => w.Length > 0)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]);
        var dir = string.Concat(words);
        return dir.Length switch
        {
            0 => "Unnamed",
            > 48 => dir[..48],
            _ => dir,
        };
    }

    private static string RewriteDirectUrl(string url)
    {
        // Dropbox share links need dl=1 to become direct downloads.
        if (url.Contains("dropbox.com", StringComparison.OrdinalIgnoreCase))
            return url.Replace("dl=0", "dl=1");
        return url;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
