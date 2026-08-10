namespace Mri.Curation.Parsing;

public enum RowKind
{
    Mod,
    CategoryHeader,
    Empty,

    /// <summary>A continuation/notes row (no name, no usable link).</summary>
    Note,
}

public sealed record ParsedRow
{
    /// <summary>1-based row number in the source CSV (header = row 1).</summary>
    public required int CsvRow { get; init; }

    public required RowKind Kind { get; init; }
    public string Category { get; init; } = "";
    public string Name { get; init; } = "";
    public string? PageLink { get; init; }
    public string? DownloadLink { get; init; }
    public string? Version { get; init; }
    public string? Comment { get; init; }

    // Derived at parse time:
    public string? Handler { get; init; }
    public int? NexusId { get; init; }
    public long? NexusFileId { get; init; }
    public string Slug { get; init; } = "";
}
