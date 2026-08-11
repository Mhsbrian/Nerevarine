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
/// Classifier for umo's real stdout/stderr, built from captured field output
/// (see Fixtures/umo). Known shapes:
///   "\e[32msyncing NAME\e[0m"                            — per-mod progress
///   "\e[31mNNN - NAME:\e[0m" + "\e[31m- error …\e[0m"    — failure header + detail
///   "\e[31mNo mod file found for \"NAME/FILE\" …\e[0m"   — unmatchable Nexus file
/// Red (31m) marks umo's error stream; that color signal gates the failure
/// patterns so mod descriptions containing the word "error" never match.
/// Unknown lines degrade to Info and still reach the log verbatim.
/// </summary>
public static partial class UmoProgressParser
{
    [GeneratedRegex(@"\x1b\[[0-9;]*m")]
    private static partial Regex AnsiRegex();

    [GeneratedRegex(@"\((?<current>\d+)\s*/\s*(?<total>\d+)\)|\[(?<current2>\d+)\s*/\s*(?<total2>\d+)\]")]
    private static partial Regex CounterRegex();

    [GeneratedRegex(@"^syncing\s+(?<name>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SyncingRegex();

    [GeneratedRegex(@"^\d+\s+-\s+(?<name>.+):$")]
    private static partial Regex FailureHeaderRegex();

    [GeneratedRegex(@"No mod file found for ""(?<name>[^/""]+)/", RegexOptions.IgnoreCase)]
    private static partial Regex NoModFileRegex();

    [GeneratedRegex(@"^(?:downloading|fetching)\s+(?<name>.+?)(?:\s*\.{3}|\s*$)", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadRegex();

    [GeneratedRegex(@"^(?:extracting|unpacking)\s+(?<name>.+?)(?:\s*\.{3}|\s*$)", RegexOptions.IgnoreCase)]
    private static partial Regex ExtractRegex();

    public static string StripAnsi(string line) => AnsiRegex().Replace(line, "");

    public static UmoEvent Parse(string rawLine)
    {
        var isRed = rawLine.Contains("\x1b[31m", StringComparison.Ordinal);
        var line = StripAnsi(rawLine).Trim();
        var (current, total) = ExtractCounter(line);

        if (isRed)
        {
            if (NoModFileRegex().Match(line) is { Success: true } noFile)
                return new UmoEvent(UmoEventKind.ModFailed, line, noFile.Groups["name"].Value.Trim());
            if (FailureHeaderRegex().Match(line) is { Success: true } header)
                return new UmoEvent(UmoEventKind.ModFailed, line, header.Groups["name"].Value.Trim());
            // Red detail lines ("- error …") carry no mod name; the header did.
            return new UmoEvent(UmoEventKind.Info, line);
        }

        if (SyncingRegex().Match(line) is { Success: true } syncing)
            return new UmoEvent(UmoEventKind.Download, line, syncing.Groups["name"].Value.Trim(), current, total);

        if (DownloadRegex().Match(line) is { Success: true } download)
            return new UmoEvent(UmoEventKind.Download, line, download.Groups["name"].Value.Trim(), current, total);

        if (ExtractRegex().Match(line) is { Success: true } extract)
            return new UmoEvent(UmoEventKind.Extract, line, extract.Groups["name"].Value.Trim(), current, total);

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
}
