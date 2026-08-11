using System.Security.Cryptography;
using System.Text.Json;
using Mri.Core.IO;

namespace Mri.Core.Tools;

public sealed record ToolProgress(string ToolId, string Phase, long BytesDone, long? BytesTotal);

/// <summary>
/// Downloads and unpacks each tool from tools.json into
/// &lt;toolsRoot&gt;/&lt;installSubdir&gt;. Idempotent: a version marker plus a live exe
/// probe short-circuit completed tools; a missing exe with an intact marker
/// (antivirus quarantine — the classic umo.exe failure) triggers re-extraction.
/// </summary>
public sealed class ToolAcquisitionService(
    HttpClient http,
    IProcessRunner runner,
    string toolsRoot)
{
    private const string MarkerFileName = ".mri-tool.json";

    public string ToolsRoot { get; } = toolsRoot;
    private string DownloadsDir => Path.Combine(ToolsRoot, "_downloads");

    public string GetToolDir(ToolSpec tool) => Path.Combine(ToolsRoot, tool.InstallSubdir);

    /// <summary>Recursively finds the probe exe — pack layouts move around between versions.</summary>
    public string? FindExe(ToolSpec tool) => FindExe(tool, tool.Variant.ExeProbe);

    public string? FindExe(ToolSpec tool, string exeName)
    {
        var dir = GetToolDir(tool);
        if (!Directory.Exists(dir))
            return null;
        // Shallowest match wins: the MOMW pack has a root umo.exe launcher AND
        // an inner umo/bin/umo.exe — the root one is the supported entry point.
        return Directory.EnumerateFiles(dir, exeName, SearchOption.AllDirectories)
            .OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
            .ThenBy(p => p.Length)
            .FirstOrDefault();
    }

    public bool IsInstalled(ToolSpec tool) =>
        MarkerMatches(tool) && FindExe(tool) is not null;

    public async Task EnsureAllAsync(
        ToolManifest manifest,
        IProgress<ToolProgress>? progress = null,
        CancellationToken ct = default)
    {
        foreach (var tool in manifest.Tools)
            await EnsureToolAsync(tool, progress, ct).ConfigureAwait(false);
    }

    public async Task EnsureToolAsync(
        ToolSpec tool,
        IProgress<ToolProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (IsInstalled(tool))
            return;

        Directory.CreateDirectory(DownloadsDir);
        var archivePath = Path.Combine(DownloadsDir, FileNameFor(tool));

        if (!File.Exists(archivePath) || !await HashMatchesAsync(archivePath, tool.Variant.Sha256, ct).ConfigureAwait(false))
        {
            await DownloadAsync(tool, archivePath, progress, ct).ConfigureAwait(false);
            if (!await HashMatchesAsync(archivePath, tool.Variant.Sha256, ct).ConfigureAwait(false))
                throw new InvalidDataException(
                    $"Downloaded {tool.Id} does not match its pinned sha256 — refusing to install it.");
        }

        progress?.Report(new ToolProgress(tool.Id, "extract", 0, null));
        await ExtractAsync(tool, archivePath, ct).ConfigureAwait(false);

        if (FindExe(tool) is null)
            throw new InvalidOperationException(
                $"{tool.Id} was extracted but '{tool.Variant.ExeProbe}' was not found anywhere under " +
                $"'{GetToolDir(tool)}' — either the archive layout changed or an antivirus " +
                "quarantined the binary.");

        WriteMarker(tool);
        progress?.Report(new ToolProgress(tool.Id, "done", 0, null));
    }

    private async Task DownloadAsync(
        ToolSpec tool, string archivePath, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        using var response = await http.GetAsync(tool.Variant.Url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;

        var tmpPath = archivePath + ".partial";
        await using (var target = File.Create(tmpPath))
        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                progress?.Report(new ToolProgress(tool.Id, "download", done, total));
            }
        }

        File.Move(tmpPath, archivePath, overwrite: true);
    }

    private async Task ExtractAsync(ToolSpec tool, string archivePath, CancellationToken ct)
    {
        var toolDir = GetToolDir(tool);
        var extractor = new ArchiveExtractor(runner);

        switch (tool.Variant.ArchiveType)
        {
            case ToolArchiveType.Zip:
                extractor.ExtractZip(archivePath, toolDir);
                break;

            case ToolArchiveType.TarGz:
                extractor.ExtractTarGz(archivePath, toolDir);
                break;

            case ToolArchiveType.NsisExe:
                await ExtractNsisAsync(tool, archivePath, toolDir, extractor, ct).ConfigureAwait(false);
                break;

            case ToolArchiveType.SevenZip:
                var sevenZip = Find7zAnywhere()
                    ?? throw new InvalidOperationException(
                        "A 7z executable is required before 7z archives can be extracted.");
                await extractor.Extract7zAsync(sevenZip, archivePath, toolDir, null, ct).ConfigureAwait(false);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(tool), tool.Variant.ArchiveType, null);
        }
    }

    /// <summary>
    /// NSIS installers are 7z-readable archives, and extracting beats running
    /// them: no elevation, no registry writes, and immunity to the /D= switch's
    /// no-quoting rule — a quoted /D= path (which is what argument quoting
    /// produces for any path with a space, e.g. "C:\Morrowind Renewed\...") is
    /// silently IGNORED by NSIS, sending the install to its default location
    /// while we probe an empty folder. The silent installer remains only as a
    /// fallback when no 7z is available, with the path passed unquoted.
    /// </summary>
    private async Task ExtractNsisAsync(
        ToolSpec tool, string archivePath, string toolDir, ArchiveExtractor extractor, CancellationToken ct)
    {
        if (Find7zAnywhere() is { } sevenZip)
        {
            try
            {
                await extractor.Extract7zAsync(sevenZip, archivePath, toolDir, null, ct).ConfigureAwait(false);
                CleanupNsisArtifacts(toolDir);
                return;
            }
            catch (IOException)
            {
                // 7z couldn't read this particular installer — fall through.
            }
        }

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                $"{tool.Id} is an NSIS installer; extracting it requires the bundled 7z " +
                "(is the tools pack listed before it in tools.json?).");

        // NSIS silent switches: /S must be uppercase; /D= must be the LAST
        // argument and UNQUOTED even when the path contains spaces — hence
        // RawArguments instead of Args.
        var result = await runner.RunAsync(new ProcessSpec
        {
            Exe = archivePath,
            RawArguments = $"/S /D={toolDir}",
            Timeout = TimeSpan.FromMinutes(10),
        }, null, ct).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException(
                $"Silent install of {tool.Id} failed with exit code {result.ExitCode}.");
    }

    /// <summary>Drops NSIS runtime leftovers ($PLUGINSDIR etc.) and uninstaller stubs.</summary>
    private static void CleanupNsisArtifacts(string toolDir)
    {
        foreach (var dir in Directory.EnumerateDirectories(toolDir, "$*").ToList())
            Directory.Delete(dir, recursive: true);
        foreach (var name in new[] { "Uninstall.exe", "uninst.exe", "uninstall.exe" })
        {
            var stub = Path.Combine(toolDir, name);
            if (File.Exists(stub))
                File.Delete(stub);
        }
    }

    /// <summary>
    /// The MOMW tools pack ships a RAR-capable 7-Zip build named 7zmo.exe;
    /// fall back to a stock 7z if one is around.
    /// </summary>
    public string? Find7zAnywhere()
    {
        if (!Directory.Exists(ToolsRoot))
            return null;
        foreach (var name in OperatingSystem.IsWindows()
                     ? new[] { "7zmo.exe", "7z.exe" }
                     : new[] { "7zmo", "7z" })
        {
            var found = Directory.EnumerateFiles(ToolsRoot, name, SearchOption.AllDirectories)
                .FirstOrDefault(p => !p.Contains("_downloads"));
            if (found is not null)
                return found;
        }
        return null;
    }

    private static string FileNameFor(ToolSpec tool)
    {
        var extension = tool.Variant.ArchiveType switch
        {
            ToolArchiveType.Zip => ".zip",
            ToolArchiveType.NsisExe => ".exe",
            ToolArchiveType.SevenZip => ".7z",
            ToolArchiveType.TarGz => ".tar.gz",
            _ => ".bin",
        };
        return $"{tool.Id}-{tool.Version}{extension}";
    }

    private static async Task<bool> HashMatchesAsync(string path, string? expectedSha256, CancellationToken ct)
    {
        if (expectedSha256 is null)
            return true; // Unpinned (e.g. moving master artifact) — trust and record.
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
        return string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private string MarkerPath(ToolSpec tool) => Path.Combine(GetToolDir(tool), MarkerFileName);

    private bool MarkerMatches(ToolSpec tool)
    {
        try
        {
            if (!File.Exists(MarkerPath(tool)))
                return false;
            var marker = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(MarkerPath(tool)));
            return marker is not null &&
                   marker.GetValueOrDefault("version") == tool.Version &&
                   marker.GetValueOrDefault("url") == tool.Variant.Url;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void WriteMarker(ToolSpec tool) =>
        AtomicFile.WriteAllText(MarkerPath(tool), JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["id"] = tool.Id,
            ["version"] = tool.Version,
            ["url"] = tool.Variant.Url,
            ["installedAt"] = DateTimeOffset.UtcNow.ToString("O"),
        }, new JsonSerializerOptions { WriteIndented = true }));
}
