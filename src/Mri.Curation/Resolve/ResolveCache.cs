using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mri.Curation.Resolve;

/// <summary>
/// Committed cache of Nexus API lookups (data/resolve-cache.json) so the
/// 100/hr / 2500/day rate budget is spent once, ever. Keyed by mod id.
/// </summary>
public sealed class ResolveCache
{
    public Dictionary<string, CachedMod> Mods { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public CachedMod? Get(int nexusId) => Mods.GetValueOrDefault(nexusId.ToString());

    public void Put(int nexusId, CachedMod mod) => Mods[nexusId.ToString()] = mod;

    public static ResolveCache LoadFile(string path) =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<ResolveCache>(File.ReadAllText(path), JsonOptions) ?? new ResolveCache()
            : new ResolveCache();

    public void SaveFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}

public sealed class CachedMod
{
    public string? Name { get; set; }
    public string? Author { get; set; }
    public string? Version { get; set; }
    public bool Available { get; set; } = true;
    public string? FetchedAt { get; set; }
    public List<CachedFile> Files { get; set; } = [];
}

public sealed class CachedFile
{
    public long FileId { get; set; }
    public string Name { get; set; } = "";
    public string? FileName { get; set; }
    public string? Version { get; set; }
    public long SizeBytes { get; set; }
    public string? Category { get; set; } // MAIN | UPDATE | OPTIONAL | OLD_VERSION | MISCELLANEOUS
    public long UploadedTimestamp { get; set; }
}
