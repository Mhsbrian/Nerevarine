namespace Mri.Core.GameDetection;

public sealed record GameValidation
{
    public required bool IsValid { get; init; }
    public string? GameRoot { get; init; }
    public string? DataFilesDir { get; init; }
    public string? MorrowindIniPath { get; init; }
    public bool HasTribunal { get; init; }
    public bool HasBloodmoon { get; init; }
    public string? FailReason { get; init; }
}

/// <summary>
/// Same ground truth as OpenMW's own wizard: a real Morrowind install is a
/// "Data Files" directory containing Morrowind.esm and Morrowind.bsa
/// (case-insensitive — installs restored from backups on other filesystems do
/// drift in casing). Accepts either the game root or the Data Files directory.
/// </summary>
public static class GameValidator
{
    public static GameValidation Validate(string directory)
    {
        if (!Directory.Exists(directory))
            return Fail($"Directory does not exist: {directory}");

        directory = Path.GetFullPath(directory);

        // Passed the game root? Look for a "Data Files" child.
        // Passed "Data Files" itself? Its parent is the game root.
        var dataFiles = FindEntryCI(directory, "Data Files", directories: true);
        string gameRoot;
        if (dataFiles is null)
        {
            if (FindEntryCI(directory, "Morrowind.esm", directories: false) is not null)
            {
                dataFiles = directory;
                gameRoot = Path.GetDirectoryName(directory) ?? directory;
            }
            else
            {
                return Fail("No 'Data Files' folder (and no Morrowind.esm) found here.");
            }
        }
        else
        {
            gameRoot = directory;
        }

        var esm = FindEntryCI(dataFiles, "Morrowind.esm", directories: false);
        var bsa = FindEntryCI(dataFiles, "Morrowind.bsa", directories: false);
        if (esm is null || bsa is null)
        {
            return Fail(
                $"'{dataFiles}' is missing {(esm is null ? "Morrowind.esm" : "Morrowind.bsa")} — " +
                "not a complete Morrowind installation.");
        }

        return new GameValidation
        {
            IsValid = true,
            GameRoot = gameRoot,
            DataFilesDir = dataFiles,
            MorrowindIniPath = FindEntryCI(gameRoot, "Morrowind.ini", directories: false),
            HasTribunal = FindEntryCI(dataFiles, "Tribunal.esm", directories: false) is not null,
            HasBloodmoon = FindEntryCI(dataFiles, "Bloodmoon.esm", directories: false) is not null,
        };
    }

    /// <summary>
    /// Case-insensitive lookup that works on case-sensitive filesystems
    /// (Linux dev machines, Proton prefixes) where File.Exists would miss
    /// "MORROWIND.ESM".
    /// </summary>
    private static string? FindEntryCI(string dir, string name, bool directories)
    {
        try
        {
            var entries = directories
                ? Directory.EnumerateDirectories(dir)
                : Directory.EnumerateFiles(dir);
            return entries.FirstOrDefault(e =>
                string.Equals(Path.GetFileName(e), name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static GameValidation Fail(string reason) =>
        new() { IsValid = false, FailReason = reason };
}
