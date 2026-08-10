namespace Mri.Core.Modlist;

/// <summary>
/// The canonical modlist. Array order is THE authority for both data= and
/// content= ordering in openmw.cfg (later data wins — that is how
/// "needs to overwrite" is expressed). Everything else (umo ModDesc JSON,
/// LoadOrderPlan) is a projection of this model.
/// </summary>
public sealed record Modlist
{
    public int SchemaVersion { get; init; } = 1;
    public required string ListVersion { get; init; }
    public required string Name { get; init; }
    public long EstimatedDownloadBytes { get; init; }
    public long EstimatedInstalledBytes { get; init; }
    public IReadOnlyList<ModEntry> Mods { get; init; } = Array.Empty<ModEntry>();
}

public enum ModHandler
{
    Nexus,
    Github,
    Direct,
}

public sealed record ModSource
{
    public required ModHandler Handler { get; init; }
    public required string Url { get; init; }
    public string NexusGame { get; init; } = "morrowind";
    public int? NexusId { get; init; }
}

public enum FileActionType
{
    Remove,
    Rename,
    Copy,
    Clean,
}

/// <summary>
/// Post-extraction file fix-ups, matching umo's action vocabulary
/// (remove/rename/copy/clean-via-tes3cmd). Paths are relative to the mod
/// base directory, same convention as umo.
/// </summary>
public sealed record FileAction
{
    public required FileActionType Type { get; init; }
    public string? Path { get; init; }
    public IReadOnlyList<string>? Paths { get; init; }
    public string? Src { get; init; }
    public string? Dst { get; init; }
    public bool Force { get; init; }
    public IReadOnlyList<string>? Arguments { get; init; }
}

public sealed record ModDownload
{
    public required string FileName { get; init; }
    public long? NexusFileId { get; init; }
    public bool Pinned { get; init; } = true;
    public string? DirectUrl { get; init; }
    public long SizeBytes { get; init; }
    public required string ExtractTo { get; init; }
    public IReadOnlyList<FileAction> Actions { get; init; } = Array.Empty<FileAction>();
}

public enum ContentMode
{
    /// <summary>Gets a content= line.</summary>
    Normal,

    /// <summary>Input to the delta-plugin merge only: content= in phase 1, dropped once delta-merged.omwaddon exists.</summary>
    DeltaOnly,

    /// <summary>Installed on disk but never activated.</summary>
    Disabled,
}

public sealed record ContentFile
{
    public required string File { get; init; }
    public ContentMode Mode { get; init; } = ContentMode.Normal;
}

public sealed record ModProvenance
{
    public int CsvRow { get; init; }
    public string? ResolvedVersion { get; init; }
    public string? ResolvedAt { get; init; }
}

public sealed record ModEntry
{
    /// <summary>Stable slug — the key used by overrides, state.json and skip lists.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }
    public required string Category { get; init; }
    public string? Author { get; init; }
    public required ModSource Source { get; init; }
    public IReadOnlyList<ModDownload> Downloads { get; init; } = Array.Empty<ModDownload>();

    /// <summary>Directories (relative to the mods root) that become data= lines, in this order.</summary>
    public IReadOnlyList<string> DataPaths { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ContentFile> Content { get; init; } = Array.Empty<ContentFile>();
    public IReadOnlyList<string> Groundcover { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> BsaArchives { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>Raw COMMENT prose from the source spreadsheet — audit trail only.</summary>
    public string? Notes { get; init; }

    public ModProvenance? Provenance { get; init; }
}
