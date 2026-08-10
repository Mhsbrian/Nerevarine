using Mri.Core.Modlist;
using Mri.Curation.Overrides;

namespace Mri.Curation.Emit;

/// <summary>Mutable working copy of a mod entry while parsing/overrides run.</summary>
public sealed class DraftMod
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    public string? Author { get; set; }
    public required string Handler { get; set; }
    public required string Url { get; set; }
    public int? NexusId { get; set; }
    public long? NexusFileId { get; set; }
    public string? DirectUrl { get; set; }
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public required string ExtractTo { get; set; }
    public List<string> DataPaths { get; set; } = [];
    public List<(string File, ContentMode Mode)> Content { get; set; } = [];
    public List<string> Groundcover { get; set; } = [];
    public List<string> BsaArchives { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public List<ActionSpec> Actions { get; set; } = [];
    public string? Notes { get; set; }
    public string? Version { get; set; }
    public int CsvRow { get; set; }
    public string? ResolvedVersion { get; set; }
    public string? ResolvedAt { get; set; }

    public bool Skipped { get; set; }
    public string? SkipReason { get; set; }
    public List<ConstraintSpec> Constraints { get; set; } = [];

    /// <summary>Open review items; the emit report lists every one of these.</summary>
    public List<string> Problems { get; set; } = [];

    public ModEntry ToModEntry() => new()
    {
        Id = Id,
        Name = Name,
        Category = Category,
        Author = Author,
        Source = new ModSource
        {
            Handler = Handler switch
            {
                "nexus" => ModHandler.Nexus,
                "github" => ModHandler.Github,
                _ => ModHandler.Direct,
            },
            Url = Url,
            NexusId = NexusId,
        },
        Downloads =
        [
            new ModDownload
            {
                FileName = FileName.Length > 0 ? FileName : Name,
                NexusFileId = NexusFileId,
                Pinned = NexusFileId is not null,
                DirectUrl = DirectUrl,
                SizeBytes = SizeBytes,
                ExtractTo = ExtractTo,
                Actions = Actions.Select(ToFileAction).ToList(),
            },
        ],
        DataPaths = DataPaths.ToList(),
        Content = Content.Select(c => new ContentFile { File = c.File, Mode = c.Mode }).ToList(),
        Groundcover = Groundcover.ToList(),
        BsaArchives = BsaArchives.ToList(),
        Tags = Tags.ToList(),
        Notes = Notes,
        Provenance = new ModProvenance
        {
            CsvRow = CsvRow,
            ResolvedVersion = ResolvedVersion,
            ResolvedAt = ResolvedAt,
        },
    };

    public static FileAction ToFileAction(ActionSpec spec) => new()
    {
        Type = spec.Type.ToLowerInvariant() switch
        {
            "remove" => FileActionType.Remove,
            "rename" => FileActionType.Rename,
            "copy" => FileActionType.Copy,
            "clean" => FileActionType.Clean,
            _ => throw new InvalidDataException($"Unknown override action type '{spec.Type}'."),
        },
        Path = spec.Path,
        Paths = spec.Paths,
        Src = spec.Src,
        Dst = spec.Dst,
        Force = spec.Force,
        Arguments = spec.Arguments,
    };

    public static ContentMode ParseMode(string mode) => mode.ToLowerInvariant() switch
    {
        "normal" or "" => ContentMode.Normal,
        "deltaonly" => ContentMode.DeltaOnly,
        "disabled" => ContentMode.Disabled,
        _ => throw new InvalidDataException($"Unknown content mode '{mode}'."),
    };
}
