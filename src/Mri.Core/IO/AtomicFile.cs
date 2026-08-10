namespace Mri.Core.IO;

/// <summary>
/// Write-temp-then-rename file writes so a crash mid-write never leaves a
/// truncated config behind.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);

        var tmp = Path.Combine(dir, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tmp, contents);
            if (File.Exists(fullPath))
                File.Replace(tmp, fullPath, destinationBackupFileName: null);
            else
                File.Move(tmp, fullPath);
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }

    public static void WriteAllLines(string path, IEnumerable<string> lines) =>
        WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
}
