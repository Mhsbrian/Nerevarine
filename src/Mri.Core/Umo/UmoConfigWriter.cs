using System.Text.Json;
using Mri.Core.IO;

namespace Mri.Core.Umo;

public sealed record UmoSettings
{
    /// <summary>umo's BASEPATH — where mods get installed.</summary>
    public required string ModBaseDir { get; init; }

    /// <summary>umo's CACHE_DIR — where downloaded archives are kept.</summary>
    public required string CacheDir { get; init; }

    /// <summary>Absolute path to tes3cmd.exe (a required key in umo's config).</summary>
    public string Tes3cmdPath { get; init; } = "";
}

/// <summary>
/// Pre-writes umo's config.json into an installer-owned directory. Every umo
/// invocation gets UMO_CONF_DIR pointed here, so the user's own
/// %APPDATA%\umomwd (if any) is never touched.
///
/// Schema comes straight from umo 0.11.x source (umo.py load_config /
/// check_config): UPPERCASE keys NEXUS_API_KEY / TES3CMD / BASEPATH are
/// required (KeyError otherwise — confirmed by a field traceback), CACHE_DIR
/// is optional. NEXUS_API_KEY is deliberately written EMPTY: the real key is
/// injected per-process via the UMO_NEXUS_API_KEY environment override, so it
/// never rests on disk in plaintext.
/// </summary>
public sealed class UmoConfigWriter(string confDir)
{
    public string ConfDir { get; } = confDir;
    public string ConfigPath => Path.Combine(ConfDir, "config.json");

    public string ExpectedJson(UmoSettings settings) =>
        JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["NEXUS_API_KEY"] = "",
            ["TES3CMD"] = settings.Tes3cmdPath.Length > 0
                ? Path.GetFullPath(settings.Tes3cmdPath)
                : "",
            ["BASEPATH"] = Path.GetFullPath(settings.ModBaseDir),
            ["CACHE_DIR"] = Path.GetFullPath(settings.CacheDir),
        }, new JsonSerializerOptions { WriteIndented = true });

    public void Write(UmoSettings settings)
    {
        Directory.CreateDirectory(ConfDir);
        AtomicFile.WriteAllText(ConfigPath, ExpectedJson(settings));
    }

    /// <summary>
    /// Content comparison, not mere existence: a config written by an older
    /// build with a wrong schema must be detected as stale and rewritten.
    /// </summary>
    public bool IsCurrent(UmoSettings settings) =>
        File.Exists(ConfigPath) && File.ReadAllText(ConfigPath) == ExpectedJson(settings);
}
