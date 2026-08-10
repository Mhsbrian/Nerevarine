using System.Text.Json;
using Mri.Core.IO;

namespace Mri.Core.Umo;

public sealed record UmoSettings
{
    public required string NexusApiKey { get; init; }
    public required string ModBaseDir { get; init; }
    public required string CacheDir { get; init; }
    public string? Tes3cmdPath { get; init; }
}

/// <summary>
/// Pre-writes umo's config.json into an installer-owned directory. Every umo
/// invocation gets UMO_CONF_DIR pointed here, so the user's own
/// %APPDATA%\umomwd (if any) is never touched.
///
/// NOTE: key names mirror what `umo setup` writes as of umo 0.11.x; they are
/// re-verified against the real binary in the M1 smoke run.
/// </summary>
public sealed class UmoConfigWriter(string confDir)
{
    public string ConfDir { get; } = confDir;
    public string ConfigPath => Path.Combine(ConfDir, "config.json");

    public void Write(UmoSettings settings)
    {
        Directory.CreateDirectory(ConfDir);
        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["apikey"] = settings.NexusApiKey,
            ["base_path"] = settings.ModBaseDir,
            ["cache_path"] = settings.CacheDir,
            ["tes3cmd"] = settings.Tes3cmdPath,
        }, new JsonSerializerOptions { WriteIndented = true });
        AtomicFile.WriteAllText(ConfigPath, json);
    }

    public bool Exists() => File.Exists(ConfigPath);
}
