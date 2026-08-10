using System.Text.RegularExpressions;

namespace Mri.Core.Umo;

public enum UmoEventKind
{
    Info,
    Download,
    Extract,
    ModCompleted,
    ModFailed,
    Progress,
}

public sealed record UmoEvent(
    UmoEventKind Kind,
    string RawLine,
    string? ModName = null,
    int? Current = null,
    int? Total = null);

/// <summary>
/// Tolerant classifier for umo's stdout. umo's exact output format is not a
/// stable contract, so unknown lines degrade to Info (and still reach the log
/// verbatim) rather than breaking the install. Refined against captured real
/// fixtures from the M1 smoke run.
/// </summary>
public static partial class UmoProgressParser
{
    [GeneratedRegex(@"\((?<current>\d+)\s*/\s*(?<total>\d+)\)|\b(?<current2>\d+)\s*/\s*(?<total2>\d+)\b")]
    private static partial Regex CounterRegex();

    [GeneratedRegex(@"(?:downloading|fetching)\s+(?<name>.+?)(?:\s*\.{3}|\s*$)", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadRegex();

    [GeneratedRegex(@"(?:extracting|unpacking)\s+(?<name>.+?)(?:\s*\.{3}|\s*$)", RegexOptions.IgnoreCase)]
    private static partial Regex ExtractRegex();

    [GeneratedRegex(@"(?:failed|error)[:\s]+(?<name>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex FailureRegex();

    [GeneratedRegex(@"(?:installed|done|finished|completed)[:\s]+(?<name>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex CompletedRegex();

    public static UmoEvent Parse(string line)
    {
        var (current, total) = ExtractCounter(line);

        if (FailureRegex().Match(line) is { Success: true } failure)
            return new UmoEvent(UmoEventKind.ModFailed, line, failure.Groups["name"].Value.Trim(), current, total);

        if (DownloadRegex().Match(line) is { Success: true } download)
            return new UmoEvent(UmoEventKind.Download, line, CleanName(download.Groups["name"].Value), current, total);

        if (ExtractRegex().Match(line) is { Success: true } extract)
            return new UmoEvent(UmoEventKind.Extract, line, CleanName(extract.Groups["name"].Value), current, total);

        if (CompletedRegex().Match(line) is { Success: true } completed)
            return new UmoEvent(UmoEventKind.ModCompleted, line, CleanName(completed.Groups["name"].Value), current, total);

        if (current is not null)
            return new UmoEvent(UmoEventKind.Progress, line, null, current, total);

        return new UmoEvent(UmoEventKind.Info, line);
    }

    private static (int? Current, int? Total) ExtractCounter(string line)
    {
        var match = CounterRegex().Match(line);
        if (!match.Success)
            return (null, null);

        var currentGroup = match.Groups["current"].Success ? match.Groups["current"] : match.Groups["current2"];
        var totalGroup = match.Groups["total"].Success ? match.Groups["total"] : match.Groups["total2"];
        return (int.Parse(currentGroup.Value), int.Parse(totalGroup.Value));
    }

    private static string CleanName(string raw)
    {
        var name = raw.Trim();
        // Strip trailing counters like "(3/617)" that rode along with the name.
        name = CounterRegex().Replace(name, "").Trim(' ', '-', ':', '(', ')');
        return name;
    }
}
