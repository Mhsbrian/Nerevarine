using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace Mri.Core.Logging;

/// <summary>
/// Packs everything needed to debug an install from another machine into one
/// zip: all run logs, state.json, the emitted umo modlist, the umo config
/// (API key redacted), the generated OpenMW configs, tool version markers and
/// an environment summary. Contains no secrets by construction — every text
/// entry passes through the redaction list.
/// </summary>
public static class DiagnosticsBundler
{
    public static string CreateZip(
        string installDir,
        string openMwConfigDir,
        IEnumerable<string?> secrets)
    {
        var redactions = secrets
            .Where(s => !string.IsNullOrWhiteSpace(s) && s!.Length >= 4)
            .Cast<string>()
            .ToList();

        var zipPath = Path.Combine(installDir, $"diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        var manifest = new StringBuilder();

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            void AddText(string sourcePath, string entryName)
            {
                string text;
                try
                {
                    if (!File.Exists(sourcePath))
                        return;
                    text = File.ReadAllText(sourcePath);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    manifest.AppendLine($"SKIPPED (unreadable): {entryName}");
                    return;
                }

                foreach (var secret in redactions)
                    text = text.Replace(secret, "«redacted»");

                var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(text);
                manifest.AppendLine($"included: {entryName}");
            }

            void AddGlob(string dir, string pattern, string entryPrefix, SearchOption option = SearchOption.TopDirectoryOnly)
            {
                if (!Directory.Exists(dir))
                    return;
                foreach (var file in Directory.EnumerateFiles(dir, pattern, option).OrderBy(f => f))
                {
                    var relative = Path.GetRelativePath(dir, file).Replace('\\', '/');
                    AddText(file, $"{entryPrefix}/{relative}");
                }
            }

            AddGlob(Path.Combine(installDir, "logs"), "*.log", "logs");
            AddText(Path.Combine(installDir, "state.json"), "state.json");
            AddGlob(Path.Combine(installDir, "modlist"), "*", "modlist");
            AddText(Path.Combine(installDir, "umo-conf", "config.json"), "umo-conf/config.json");

            AddText(Path.Combine(openMwConfigDir, "openmw.cfg"), "openmw-config/openmw.cfg");
            AddText(Path.Combine(openMwConfigDir, "settings.cfg"), "openmw-config/settings.cfg");
            AddText(Path.Combine(openMwConfigDir, "shaders.yaml"), "openmw-config/shaders.yaml");
            AddGlob(openMwConfigDir, "openmw.cfg.bak-*", "openmw-config");

            AddGlob(Path.Combine(installDir, "tools"), ".mri-tool.json", "tool-markers",
                SearchOption.AllDirectories);

            var envEntry = zip.CreateEntry("environment.txt", CompressionLevel.Optimal);
            using var envWriter = new StreamWriter(envEntry.Open());
            envWriter.WriteLine($"created: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
            envWriter.WriteLine($"os: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
            envWriter.WriteLine($"runtime: {RuntimeInformation.FrameworkDescription}");
            envWriter.WriteLine($"installDir: {installDir}");
            envWriter.WriteLine($"openMwConfigDir: {openMwConfigDir}");
            envWriter.WriteLine($"freeDiskBytes: {TryGetFreeDisk(installDir)}");
            envWriter.WriteLine();
            envWriter.WriteLine("--- bundle manifest ---");
            envWriter.Write(manifest.ToString());
        }

        return zipPath;
    }

    private static string TryGetFreeDisk(string dir)
    {
        try
        {
            var probe = new DirectoryInfo(dir);
            while (!probe.Exists && probe.Parent is not null)
                probe = probe.Parent;
            return new DriveInfo(probe.FullName).AvailableFreeSpace.ToString();
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return "unknown";
        }
    }
}
