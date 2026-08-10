using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mri.Core.Tools;

public sealed record ToolManifest
{
    public int SchemaVersion { get; init; } = 1;
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
}

public enum ToolArchiveType
{
    Zip,
    NsisExe,
    SevenZip,
}

public sealed record ToolSpec
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string Url { get; init; }
    public string? Sha256 { get; init; }
    public required ToolArchiveType ArchiveType { get; init; }
    public required string InstallSubdir { get; init; }

    /// <summary>Exe filename proving the tool is present (searched recursively).</summary>
    public required string ExeProbe { get; init; }
}
