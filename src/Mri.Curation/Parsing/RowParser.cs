using System.Text.RegularExpressions;
using Mri.Curation.Csv;

namespace Mri.Curation.Parsing;

/// <summary>
/// Classifies the raw spreadsheet rows: ALL-CAPS rows with no links are
/// category headers; rows with a page link are mods; the rest are notes.
/// Extracts Nexus mod/file ids and generates unique slugs.
/// </summary>
public static partial class RowParser
{
    [GeneratedRegex(@"nexusmods\.com/morrowind/mods/(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex NexusModIdRegex();

    [GeneratedRegex(@"file_id=(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex NexusFileIdRegex();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex SlugCleanRegex();

    public static List<ParsedRow> Parse(string csvText)
    {
        var rows = CsvReader.Parse(csvText);
        var result = new List<ParsedRow>();
        var category = "Uncategorized";
        var seenSlugs = new HashSet<string>();

        // Row 1 is the header row.
        for (var i = 1; i < rows.Count; i++)
        {
            var cells = rows[i];
            var csvRow = i + 1;
            var name = Cell(cells, 0).Trim();
            var pageLink = NullIfEmpty(Cell(cells, 1).Trim());
            var downloadLink = NullIfEmpty(Cell(cells, 2).Trim());
            var version = NullIfEmpty(Cell(cells, 3).Trim());
            var comment = NullIfEmpty(Cell(cells, 4).Trim());

            if (name.Length == 0 && pageLink is null && downloadLink is null && comment is null)
            {
                result.Add(new ParsedRow { CsvRow = csvRow, Kind = RowKind.Empty, Category = category });
                continue;
            }

            if (IsCategoryHeader(name, pageLink, downloadLink))
            {
                category = ToTitleCase(name);
                result.Add(new ParsedRow
                {
                    CsvRow = csvRow,
                    Kind = RowKind.CategoryHeader,
                    Category = category,
                    Name = name,
                });
                continue;
            }

            if (pageLink is null || name.Length == 0)
            {
                result.Add(new ParsedRow
                {
                    CsvRow = csvRow,
                    Kind = RowKind.Note,
                    Category = category,
                    Name = name,
                    PageLink = pageLink,
                    Comment = comment,
                });
                continue;
            }

            var nexusId = MatchInt(NexusModIdRegex(), pageLink) ?? MatchInt(NexusModIdRegex(), downloadLink);
            var fileId = MatchLong(NexusFileIdRegex(), downloadLink);

            result.Add(new ParsedRow
            {
                CsvRow = csvRow,
                Kind = RowKind.Mod,
                Category = category,
                Name = name,
                PageLink = pageLink,
                DownloadLink = downloadLink,
                Version = version,
                Comment = comment,
                Handler = ClassifyHandler(pageLink),
                NexusId = nexusId,
                NexusFileId = fileId,
                Slug = UniqueSlug(name, seenSlugs),
            });
        }

        return result;
    }

    public static string ClassifyHandler(string url)
    {
        var host = SafeHost(url);
        if (host.EndsWith("nexusmods.com"))
            return "nexus";
        if (host.EndsWith("github.com") || host.EndsWith("gitlab.com"))
            return "github";
        return "direct";
    }

    private static string SafeHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : "";

    private static bool IsCategoryHeader(string name, string? pageLink, string? downloadLink) =>
        name.Length > 0 &&
        pageLink is null &&
        downloadLink is null &&
        name == name.ToUpperInvariant() &&
        name.Any(char.IsLetter);

    public static string Slugify(string name)
    {
        var slug = SlugCleanRegex().Replace(name.ToLowerInvariant(), "-").Trim('-');
        return slug.Length == 0 ? "unnamed" : slug;
    }

    private static string UniqueSlug(string name, HashSet<string> seen)
    {
        var baseSlug = Slugify(name);
        var slug = baseSlug;
        var n = 2;
        while (!seen.Add(slug))
            slug = $"{baseSlug}-{n++}";
        return slug;
    }

    private static string ToTitleCase(string allCaps) =>
        System.Globalization.CultureInfo.InvariantCulture.TextInfo
            .ToTitleCase(allCaps.ToLowerInvariant());

    private static string Cell(string[] cells, int index) =>
        index < cells.Length ? cells[index] : "";

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static int? MatchInt(Regex regex, string? input) =>
        input is not null && regex.Match(input) is { Success: true } m
            ? int.Parse(m.Groups["id"].Value)
            : null;

    private static long? MatchLong(Regex regex, string? input) =>
        input is not null && regex.Match(input) is { Success: true } m
            ? long.Parse(m.Groups["id"].Value)
            : null;
}
