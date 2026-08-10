namespace Mri.Core.GameDetection;

public sealed record SteamGame(string InstallDir, string LibraryPath, string ManifestPath);

/// <summary>
/// Finds the Steam install of Morrowind GOTY (AppID 22320):
/// registry → steam root(s) → libraryfolders.vdf (old and new formats) →
/// appmanifest_22320.acf "installdir" → steamapps/common/&lt;installdir&gt;.
/// The folder name is never hardcoded — it comes from the manifest.
/// </summary>
public sealed class SteamLocator(IRegistryReader registry)
{
    public const int MorrowindAppId = 22320;

    public IReadOnlyList<string> FindSteamRoots()
    {
        var candidates = new List<string?>
        {
            registry.GetString(RegistryRoot.CurrentUser, RegistryWidth.Registry64, @"Software\Valve\Steam", "SteamPath"),
            registry.GetString(RegistryRoot.CurrentUser, RegistryWidth.Registry32, @"Software\Valve\Steam", "SteamPath"),
            registry.GetString(RegistryRoot.LocalMachine, RegistryWidth.Registry32, @"SOFTWARE\Valve\Steam", "InstallPath"),
            registry.GetString(RegistryRoot.LocalMachine, RegistryWidth.Registry64, @"SOFTWARE\Valve\Steam", "InstallPath"),
            @"C:\Program Files (x86)\Steam",
        };

        return candidates
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => Path.GetFullPath(c!.Replace('/', Path.DirectorySeparatorChar)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .ToList();
    }

    /// <summary>
    /// All Steam library roots reachable from a Steam install dir. The Steam
    /// root itself is always included — it is not guaranteed to be listed in
    /// its own libraryfolders.vdf.
    /// </summary>
    public IReadOnlyList<string> FindLibraries(string steamRoot)
    {
        var libraries = new List<string> { steamRoot };

        var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdfPath))
        {
            try
            {
                var root = VdfParser.Parse(File.ReadAllText(vdfPath));
                var folders = root.GetChild("libraryfolders") ?? root.GetChild("LibraryFolders");
                if (folders is not null)
                {
                    // New format: numeric children with a "path" value.
                    foreach (var (key, child) in folders.Children)
                        if (key.All(char.IsDigit) && child.GetValue("path") is { } path)
                            libraries.Add(path);

                    // Old format: numeric keys mapping straight to path strings
                    // (non-numeric siblings like TimeNextStatsReport are noise).
                    foreach (var (key, value) in folders.Values)
                        if (key.All(char.IsDigit))
                            libraries.Add(value);
                }
            }
            catch (FormatException)
            {
                // Unparseable vdf: fall back to just the steam root.
            }
        }

        return libraries
            .Select(p => Path.GetFullPath(p.Replace('/', Path.DirectorySeparatorChar)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .ToList();
    }

    public SteamGame? FindApp(int appId, IEnumerable<string>? steamRoots = null)
    {
        foreach (var steamRoot in steamRoots ?? FindSteamRoots())
        {
            foreach (var library in FindLibraries(steamRoot))
            {
                var manifestPath = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");
                if (!File.Exists(manifestPath))
                    continue;

                string? installDirName;
                try
                {
                    var manifest = VdfParser.Parse(File.ReadAllText(manifestPath));
                    installDirName = manifest.GetChild("AppState")?.GetValue("installdir");
                }
                catch (FormatException)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(installDirName))
                    continue;

                var installDir = Path.Combine(library, "steamapps", "common", installDirName);
                if (Directory.Exists(installDir))
                    return new SteamGame(installDir, library, manifestPath);
            }
        }

        return null;
    }

    public SteamGame? FindMorrowind(IEnumerable<string>? steamRoots = null) =>
        FindApp(MorrowindAppId, steamRoots);
}
