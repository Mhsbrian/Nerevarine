using System.Text.Json;
using System.Text.Json.Serialization;
using Mri.Core.IO;

namespace Mri.Core.Pipeline;

/// <summary>
/// Audit record persisted to &lt;install&gt;/state.json after every step. Disk
/// artifacts are the source of truth for step verification; this file carries
/// what disk can't: which mods failed/were skipped, the harvested fallback
/// lines, and which list version this install belongs to.
/// </summary>
public sealed class InstallState
{
    public int SchemaVersion { get; set; } = 1;
    public string? ListVersion { get; set; }
    public string? GamePath { get; set; }
    public Dictionary<string, DateTimeOffset> CompletedSteps { get; set; } = [];
    public List<string> FailedMods { get; set; } = [];
    public List<string> SkippedMods { get; set; } = [];
    public List<string> FallbackLines { get; set; } = [];
}

public sealed class InstallStateStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = true,
    };

    public string Path { get; } = path;

    public InstallState Load()
    {
        try
        {
            if (!File.Exists(Path))
                return new InstallState();
            return JsonSerializer.Deserialize<InstallState>(File.ReadAllText(Path), JsonOptions)
                ?? new InstallState();
        }
        catch (JsonException)
        {
            return new InstallState(); // Corrupt state: disk verification rebuilds it.
        }
    }

    public void Save(InstallState state) =>
        AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(state, JsonOptions));

    public bool Exists() => File.Exists(Path);
}
