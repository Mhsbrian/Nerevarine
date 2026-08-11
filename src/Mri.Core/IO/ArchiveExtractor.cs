using System.Formats.Tar;
using System.IO.Compression;

namespace Mri.Core.IO;

/// <summary>
/// Zip and tar.gz extraction are native (.NET guards against path traversal,
/// and TarFile preserves Unix file modes); everything else (7z, rar, NSIS exe
/// unpacking) shells out to the RAR-capable 7z bundled in the MOMW tools pack.
/// </summary>
public sealed class ArchiveExtractor(IProcessRunner runner)
{
    public void ExtractZip(string archivePath, string destDir)
    {
        Directory.CreateDirectory(destDir);
        ZipFile.ExtractToDirectory(archivePath, destDir, overwriteFiles: true);
    }

    public void ExtractTarGz(string archivePath, string destDir)
    {
        Directory.CreateDirectory(destDir);
        using var file = File.OpenRead(archivePath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gzip, destDir, overwriteFiles: true);
    }

    public async Task Extract7zAsync(
        string sevenZipExe,
        string archivePath,
        string destDir,
        IProgress<OutputLine>? onLine = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(destDir);
        var result = await runner.RunAsync(new ProcessSpec
        {
            Exe = sevenZipExe,
            Args = ["x", "-y", $"-o{destDir}", archivePath],
        }, onLine, ct).ConfigureAwait(false);

        if (!result.Success)
            throw new IOException(
                $"7z extraction of '{Path.GetFileName(archivePath)}' failed with exit code {result.ExitCode}.");
    }
}
