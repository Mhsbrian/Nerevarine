namespace Mri.Core.Modlist;

/// <summary>
/// The flattened, ordered inputs the openmw.cfg composer consumes. Paths are
/// relative to the mods root; the composer makes them absolute.
/// </summary>
public sealed record LoadOrderPlan
{
    public required IReadOnlyList<string> DataDirs { get; init; }
    public required IReadOnlyList<string> ContentFiles { get; init; }
    public required IReadOnlyList<string> GroundcoverFiles { get; init; }
    public required IReadOnlyList<string> FallbackArchives { get; init; }
}

public sealed record LoadOrderOptions
{
    /// <summary>
    /// Phase 2: the delta-plugin merge has run, so delta-merged output is
    /// appended last and deltaOnly plugins drop their content= lines.
    /// </summary>
    public bool IncludeDelta { get; init; }

    public string DeltaDataDir { get; init; } = "delta-merged";
    public string DeltaContentFile { get; init; } = "delta-merged.omwaddon";

    /// <summary>Mods the user chose to skip after failed downloads.</summary>
    public IReadOnlySet<string> SkippedModIds { get; init; } = new HashSet<string>();

    /// <summary>
    /// Repo-shipped record-level repair plugins (data/fixups). They load
    /// after all modlist content — overriding broken records in mods we
    /// cannot edit in place — and before the delta merge so delta sees them.
    /// </summary>
    public string FixupsDataDir { get; init; } = "mri-fixups";
    public IReadOnlyList<string> FixupsContentFiles { get; init; } = [];

    /// <summary>
    /// MOMW's field-tested content order (data/momw-content-order.txt).
    /// Plugins we share with it are permuted into its relative order within
    /// the slots they already occupy; everything else keeps its position.
    /// Empty disables the pass.
    /// </summary>
    public IReadOnlyList<string> MomwContentOrder { get; init; } = [];
}
