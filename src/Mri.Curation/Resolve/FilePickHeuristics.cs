namespace Mri.Curation.Resolve;

/// <summary>
/// Chooses which Nexus file to pin when the spreadsheet gave no file_id:
/// MAIN-category files beat everything, a version match with the sheet's
/// VERSION column is strong evidence, then name similarity and recency.
/// </summary>
public static class FilePickHeuristics
{
    public static (CachedFile? Best, bool Confident) Choose(
        IReadOnlyList<CachedFile> files, string modName, string? sheetVersion)
    {
        var candidates = files
            .Where(f => f.Category is not ("OLD_VERSION" or "ARCHIVED" or "Old" or "Old File"))
            .ToList();
        if (candidates.Count == 0)
            return (null, false);
        if (candidates.Count == 1)
            return (candidates[0], true);

        var scored = candidates
            .Select(f => (File: f, Score: Score(f, modName, sheetVersion)))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.File.UploadedTimestamp)
            .ToList();

        var best = scored[0];
        var runnerUp = scored[1];
        // Confident when the winner is clearly ahead, not a coin flip.
        var confident = best.Score >= 3 && best.Score >= runnerUp.Score + 2;
        return (best.File, confident);
    }

    private static int Score(CachedFile file, string modName, string? sheetVersion)
    {
        var score = 0;
        if (string.Equals(file.Category, "MAIN", StringComparison.OrdinalIgnoreCase))
            score += 3;
        if (sheetVersion is not null && VersionsMatch(file.Version, sheetVersion))
            score += 2;
        if (NameSimilar(file.Name, modName))
            score += 1;
        return score;
    }

    private static bool VersionsMatch(string? fileVersion, string sheetVersion)
    {
        if (fileVersion is null)
            return false;
        static string Norm(string v) => v.Trim().TrimStart('v', 'V');
        return string.Equals(Norm(fileVersion), Norm(sheetVersion), StringComparison.OrdinalIgnoreCase);
    }

    private static bool NameSimilar(string fileName, string modName)
    {
        static string Norm(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var normalizedFile = Norm(fileName);
        var normalizedMod = Norm(modName);
        return normalizedFile.Contains(normalizedMod) || normalizedMod.Contains(normalizedFile);
    }
}
