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
    public string? FindExe(ToolSpec tool) => FindExe(tool, tool.ExeProbe);

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

        if (!File.Exists(archivePath) || !await HashMatchesAsync(archivePath, tool.Sha256, ct).ConfigureAwait(false))
        {
            await DownloadAsync(tool, archivePath, progress, ct).ConfigureAwait(false);
            if (!await HashMatchesAsync(archivePath, tool.Sha256, ct).ConfigureAwait(false))
                throw new InvalidDataException(
                    $"Downloaded {tool.Id} does not match its pinned sha256 — refusing to install it.");
        }

        progress?.Report(new ToolProgress(tool.Id, "extract", 0, null));
        await ExtractAsync(tool, archivePath, ct).ConfigureAwait(false);

        if (FindExe(tool) is null)
            throw new InvalidOperationException(
                $"{tool.Id} was extracted but '{tool.ExeProbe}' is missing — " +
                "an antivirus may have quarantined it.");

        WriteMarker(tool);
        progress?.Report(new ToolProgress(tool.Id, "done", 0, null));
    }

    private async Task DownloadAsync(
        ToolSpec tool, string archivePath, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        using var response = await http.GetAsync(tool.Url, HttpCompletionOption.ResponseHeadersRead, ct)
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

        switch (tool.ArchiveType)
        {
            case ToolArchiveType.Zip:
                extractor.ExtractZip(archivePath, toolDir);
                break;

            case ToolArchiveType.NsisExe:
                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException(
                        $"{tool.Id} is an NSIS installer and can only be installed on Windows.");
                // NSIS silent switches: /S must be uppercase, /D= must be last
                // and unquoted (no trailing backslash).
                var result = await runner.RunAsync(new ProcessSpec
                {
                    Exe = archivePath,
                    Args = ["/S", $"/D={toolDir}"],
                    Timeout = TimeSpan.FromMinutes(10),
                }, null, ct).ConfigureAwait(false);
                if (!result.Success)
                    throw new InvalidOperationException(
                        $"Silent install of {tool.Id} failed with exit code {result.ExitCode}.");
                break;

            case ToolArchiveType.SevenZip:
                var sevenZip = Find7zAnywhere()
                    ?? throw new InvalidOperationException(
                        "A 7z executable is required before 7z archives can be extracted.");
                await extractor.Extract7zAsync(sevenZip, archivePath, toolDir, null, ct).ConfigureAwait(false);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(tool), tool.ArchiveType, null);
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
        var extension = tool.ArchiveType switch
        {
            ToolArchiveType.Zip => ".zip",
            ToolArchiveType.NsisExe => ".exe",
            ToolArchiveType.SevenZip => ".7z",
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
                   marker.GetValueOrDefault("url") == tool.Url;
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
            ["url"] = tool.Url,
            ["installedAt"] = DateTimeOffset.UtcNow.ToString("O"),
        }, new JsonSerializerOptions { WriteIndented = true }));
}
