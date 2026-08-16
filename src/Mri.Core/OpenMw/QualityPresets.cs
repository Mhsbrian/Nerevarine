using Mri.Core.IO;

namespace Mri.Core.OpenMw;

public enum QualityTier
{
    /// <summary>Performance: weaker/older machines.</summary>
    Apprentice,

    /// <summary>Balanced.</summary>
    Adept,

    /// <summary>Everything at maximum — the curated richest experience.</summary>
    Master,
}

/// <summary>
/// The three quality tiers the launcher offers, expressed as settings.cfg
/// deltas applied over the installer's tuned template via
/// <see cref="SettingsCfgMerger"/>. Master IS the shipped template; the
/// other tiers only override the keys that dominate frame cost.
/// </summary>
public static class QualityPresets
{
    /// <summary>(section, key, value) triples per tier.</summary>
    public static IReadOnlyList<(string Section, string Key, string Value)> Overrides(QualityTier tier) => tier switch
    {
        QualityTier.Master =>
        [
            ("Camera", "viewing distance", "81920"),
            ("Shadows", "enable shadows", "true"),
            ("Shadows", "shadow map resolution", "2048"),
            ("Shadows", "number of shadow maps", "3"),
            ("Shadows", "terrain shadows", "true"),
            ("Shadows", "actor shadows", "true"),
            ("Shadows", "object shadows", "true"),
            ("Shadows", "enable indoor shadows", "true"),
            ("Shaders", "max lights", "32"),
            ("Shaders", "force per pixel lighting", "true"),
            ("Water", "reflection detail", "3"),
            ("Water", "refraction", "true"),
            ("Groundcover", "density", "1.0"),
            ("Groundcover", "rendering distance", "6144.0"),
            ("Game", "actors processing range", "8192"),
            ("Post Processing", "enabled", "true"),
        ],
        QualityTier.Adept =>
        [
            ("Camera", "viewing distance", "49152"),
            ("Shadows", "enable shadows", "true"),
            ("Shadows", "shadow map resolution", "1024"),
            ("Shadows", "number of shadow maps", "2"),
            ("Shadows", "terrain shadows", "false"),
            ("Shadows", "actor shadows", "true"),
            ("Shadows", "object shadows", "false"),
            ("Shadows", "enable indoor shadows", "true"),
            ("Shaders", "max lights", "16"),
            ("Shaders", "force per pixel lighting", "true"),
            ("Water", "reflection detail", "2"),
            ("Water", "refraction", "true"),
            ("Groundcover", "density", "0.8"),
            ("Groundcover", "rendering distance", "4096.0"),
            ("Game", "actors processing range", "7168"),
            ("Post Processing", "enabled", "true"),
        ],
        _ =>
        [
            ("Camera", "viewing distance", "24576"),
            ("Shadows", "enable shadows", "false"),
            ("Shaders", "max lights", "8"),
            ("Shaders", "force per pixel lighting", "false"),
            ("Water", "reflection detail", "0"),
            ("Water", "refraction", "false"),
            ("Groundcover", "density", "0.5"),
            ("Groundcover", "rendering distance", "2048.0"),
            ("Game", "actors processing range", "3584"),
            ("Post Processing", "enabled", "false"),
        ],
    };

    /// <summary>Applies a tier to an existing settings.cfg text, returning the new text.</summary>
    public static string Apply(string settingsCfg, QualityTier tier)
    {
        var doc = IniDocument.Parse(settingsCfg);
        foreach (var (section, key, value) in Overrides(tier))
            doc.Set(section, key, value);
        return doc.ToText();
    }

    public static void ApplyToFile(string settingsCfgPath, QualityTier tier)
    {
        var text = File.Exists(settingsCfgPath) ? File.ReadAllText(settingsCfgPath) : "";
        AtomicFile.WriteAllText(settingsCfgPath, Apply(text, tier));
    }

    /// <summary>
    /// Best-effort hardware detection. VRAM probes are platform-specific and
    /// optional; RAM + core count always work and decide on their own when
    /// VRAM is unknown.
    /// </summary>
    public static QualityTier Detect(long? vramBytes = null, long? ramBytes = null, int? cores = null)
    {
        var ram = ramBytes ?? (long)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var cpu = cores ?? Environment.ProcessorCount;
        var vramGb = (vramBytes ?? ProbeVramBytes()) / (double)(1L << 30);
        var ramGb = ram / (double)(1L << 30);

        if (vramGb >= 6 || (vramGb <= 0 && ramGb >= 24 && cpu >= 8))
            return QualityTier.Master;
        if (vramGb >= 3 || (vramGb <= 0 && ramGb >= 12 && cpu >= 4))
            return QualityTier.Adept;
        return QualityTier.Apprentice;
    }

    private static long ProbeVramBytes()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                // amdgpu/nouveau expose VRAM size in sysfs; take the largest card.
                long best = 0;
                foreach (var f in Directory.EnumerateFiles("/sys/class/drm", "mem_info_vram_total",
                             new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3 }))
                    if (long.TryParse(File.ReadAllText(f).Trim(), out var v))
                        best = Math.Max(best, v);
                return best;
            }
            if (OperatingSystem.IsWindows())
            {
                // qwMemorySize (u64) is the reliable registry field; AdapterRAM caps at 4GB.
                using var root = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                long best = 0;
                foreach (var name in root?.GetSubKeyNames() ?? [])
                {
                    if (!name.StartsWith("0")) continue;
                    using var sub = root!.OpenSubKey(name);
                    if (sub?.GetValue("HardwareInformation.qwMemorySize") is long v)
                        best = Math.Max(best, v);
                }
                return best;
            }
        }
        catch
        {
            // Detection is best-effort by contract.
        }
        return 0;
    }
}
