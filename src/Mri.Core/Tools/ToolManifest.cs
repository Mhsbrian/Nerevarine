using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mri.Core.Tools;

public sealed record ToolManifest
{
    public int SchemaVersion { get; init; } = 2;
    public IReadOnlyList<ToolSpec> Tools { get; init; } = Array.Empty<ToolSpec>();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static ToolManifest Load(string json) =>
        JsonSerializer.Deserialize<ToolManifest>(json, JsonOptions)
        ?? throw new InvalidDataException("tools.json deserialized to null.");

    public ToolSpec Get(string id) =>
        Tools.FirstOrDefault(t => t.Id == id)
        ?? throw new KeyNotFoundException($"Tool '{id}' not in manifest.");

    /// <summary>Runtime identifier used as the platform key in tools.json.</summary>
    public static string CurrentRid =>
        OperatingSystem.IsWindows() ? "win-x64"
        : OperatingSystem.IsMacOS() ? "osx-x64"
        : "linux-x64";
}

public enum ToolArchiveType
{
    Zip,
    NsisExe,
    SevenZip,
    TarGz,
}

/// <summary>Per-platform download/extraction details of a tool.</summary>
public sealed record ToolVariant
{
    public required string Url { get; init; }
    public string? Sha256 { get; init; }
    public required ToolArchiveType ArchiveType { get; init; }

    /// <summary>Binary name proving the tool is present (searched recursively).</summary>
    public required string ExeProbe { get; init; }
}

public sealed record ToolSpec
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string InstallSubdir { get; init; }
    public required IReadOnlyDictionary<string, ToolVariant> Platforms { get; init; }

    /// <summary>The variant for the machine we're running on.</summary>
    public ToolVariant Variant =>
        Platforms.GetValueOrDefault(ToolManifest.CurrentRid)
        ?? throw new PlatformNotSupportedException(
            $"Tool '{Id}' has no download for {ToolManifest.CurrentRid} in tools.json.");
}
