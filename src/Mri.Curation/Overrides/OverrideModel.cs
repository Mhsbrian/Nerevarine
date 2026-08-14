using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Mri.Curation.Overrides;

/// <summary>
/// Hand-maintained curation rules (data/overrides/overrides.yaml) that encode
/// the spreadsheet's prose COMMENT instructions as machine-readable operations.
/// </summary>
public sealed class OverridesFile
{
    public int Version { get; set; } = 1;
    public List<OverrideRule> Rules { get; set; } = [];

    public static OverridesFile Load(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        return deserializer.Deserialize<OverridesFile>(yaml) ?? new OverridesFile();
    }
}

public sealed class OverrideRule
{
    public MatchSpec Match { get; set; } = new();

    public bool Skip { get; set; }
    public string? SkipReason { get; set; }

    /// <summary>Field overrides, deep-merged onto the drafted mod.</summary>
    public SetSpec? Set { get; set; }

    /// <summary>Post-extraction file actions (remove/rename/copy/clean).</summary>
    public List<ActionSpec>? Actions { get; set; }

    /// <summary>Reposition in load order, by target slug.</summary>
    public string? MoveAfter { get; set; }
    public string? MoveBefore { get; set; }

    /// <summary>Replace one CSV row with several mod entries.</summary>
    public List<SetSpec>? SplitInto { get; set; }

    /// <summary>Assertions validated at emit time; violations fail the build.</summary>
    public List<ConstraintSpec>? Constraints { get; set; }

    /// <summary>Hint for choosing among a Nexus mod's files when no file_id is pinned.</summary>
    public PickFileSpec? PickFile { get; set; }
}

public sealed class MatchSpec
{
    public string? Slug { get; set; }
    public int? NexusId { get; set; }
    public int? CsvRow { get; set; }
    public string? Name { get; set; }
}

public sealed class SetSpec
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? ExtractTo { get; set; }
    public string? FileName { get; set; }
    public string? DirectUrl { get; set; }
    public long? NexusFileId { get; set; }
    public List<string>? DataPaths { get; set; }
    public List<ContentSpec>? Content { get; set; }
    public List<string>? Groundcover { get; set; }
    public List<string>? BsaArchives { get; set; }
    public List<string>? Tags { get; set; }
}

public sealed class ContentSpec
{
    public string File { get; set; } = "";
    public string Mode { get; set; } = "normal"; // normal | deltaOnly | disabled
}

public sealed class ActionSpec
{
    public string Type { get; set; } = ""; // remove | rename | copy | clean
    public string? Path { get; set; }
    public List<string>? Paths { get; set; }
    public string? Src { get; set; }
    public string? Dst { get; set; }
    public bool Force { get; set; }
    public List<string>? Arguments { get; set; }
}

public sealed class ConstraintSpec
{
    /// <summary>This mod's plugins must all load before the named plugin.</summary>
    public string? ContentBefore { get; set; }

    /// <summary>This mod's plugins must all load after the named plugin.</summary>
    public string? ContentAfter { get; set; }
}

public sealed class PickFileSpec
{
    public string? NameContains { get; set; }
    public string? Category { get; set; }
}
