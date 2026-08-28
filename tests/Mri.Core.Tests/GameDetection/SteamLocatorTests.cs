using Mri.Core.GameDetection;

namespace Mri.Core.Tests.GameDetection;

public class SteamLocatorTests : IDisposable
{
    private readonly string _root;

    public SteamLocatorTests() =>
        _root = Directory.CreateTempSubdirectory("mri-steam-test-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string MakeSteamRoot(string name)
    {
        var steamRoot = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
        return steamRoot;
    }

    private string MakeLibraryWithMorrowind(string name, string installDirName)
    {
        var library = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(library, "steamapps", "common", installDirName));
        File.WriteAllText(
            Path.Combine(library, "steamapps", "appmanifest_22320.acf"),
            $$"""
            "AppState"
            {
                "appid"		"22320"
                "installdir"		"{{installDirName}}"
            }
            """);
        return library;
    }

    [Fact]
    public void FindsMorrowindInSecondaryLibrary_NewVdfFormat()
    {
        var steamRoot = MakeSteamRoot("steam");
        var library = MakeLibraryWithMorrowind("SteamLibrary", "Morrowind");
        File.WriteAllText(
            Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
            $$"""
            "libraryfolders"
            {
                "0"
                {
                    "path"		"{{steamRoot.Replace(@"\", @"\\")}}"
                }
                "1"
                {
                    "path"		"{{library.Replace(@"\", @"\\")}}"
                    "apps"
                    {
                        "22320"		"2308740525"
                    }
                }
            }
            """);

        var locator = new SteamLocator(new NullRegistryReader());
        var result = locator.FindMorrowind(new[] { steamRoot });

        Assert.NotNull(result);
        Assert.Equal(Path.Combine(library, "steamapps", "common", "Morrowind"), result!.InstallDir);
        Assert.Equal(library, result.LibraryPath);
    }

    [Fact]
    public void FindsMorrowindInSecondaryLibrary_OldVdfFormat()
    {
        var steamRoot = MakeSteamRoot("steam");
        var library = MakeLibraryWithMorrowind("OldLibrary", "Morrowind GOTY");
        File.WriteAllText(
            Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
            $$"""
            "LibraryFolders"
            {
                "TimeNextStatsReport"		"1623276754"
                "1"		"{{library.Replace(@"\", @"\\")}}"
            }
            """);

        var locator = new SteamLocator(new NullRegistryReader());
        var result = locator.FindMorrowind(new[] { steamRoot });

        Assert.NotNull(result);
        Assert.Equal(Path.Combine(library, "steamapps", "common", "Morrowind GOTY"), result!.InstallDir);
    }

    [Fact]
    public void FindsMorrowindInSteamRootItself_EvenWithoutVdf()
    {
        var steamRoot = MakeLibraryWithMorrowind("steam", "Morrowind");

        var locator = new SteamLocator(new NullRegistryReader());
        var result = locator.FindMorrowind(new[] { steamRoot });

        Assert.NotNull(result);
        Assert.Equal(Path.Combine(steamRoot, "steamapps", "common", "Morrowind"), result!.InstallDir);
    }

    [Fact]
    public void IgnoresStaleLibrariesAndMissingManifests()
    {
        var steamRoot = MakeSteamRoot("steam");
        File.WriteAllText(
            Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
            """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"Z:\\RemovedDrive\\SteamLibrary"
                }
            }
            """);

        var locator = new SteamLocator(new NullRegistryReader());
        Assert.Null(locator.FindMorrowind(new[] { steamRoot }));
    }

    [Fact]
    public void LinuxSteamRootsCoverNativeSymlinkAndFlatpakHomes()
    {
        // Path.Combine uses the host separator; normalize so the well-known
        // Linux locations can be asserted literally on any build OS.
        var roots = SteamLocator.LinuxSteamRoots("/home/brian")
            .Select(r => r.Replace('\\', '/'))
            .ToList();

        Assert.Contains("/home/brian/.local/share/Steam", roots);
        Assert.Contains("/home/brian/.steam/steam", roots);
        Assert.Contains(
            "/home/brian/.var/app/com.valvesoftware.Steam/.local/share/Steam", roots);
    }

    [Fact]
    public void ManifestPointingAtMissingInstallDirIsSkipped()
    {
        var steamRoot = MakeSteamRoot("steam");
        File.WriteAllText(
            Path.Combine(steamRoot, "steamapps", "appmanifest_22320.acf"),
            """
            "AppState"
            {
                "installdir"		"Gone"
            }
            """);

        var locator = new SteamLocator(new NullRegistryReader());
        Assert.Null(locator.FindMorrowind(new[] { steamRoot }));
    }
}
