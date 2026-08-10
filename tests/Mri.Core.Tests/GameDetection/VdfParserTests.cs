using Mri.Core.GameDetection;

namespace Mri.Core.Tests.GameDetection;

public class VdfParserTests
{
    private const string NewFormatLibraryFolders = """
        "libraryfolders"
        {
            "0"
            {
                "path"		"C:\\Program Files (x86)\\Steam"
                "label"		""
                "contentid"		"1234567890123456789"
                "totalsize"		"0"
                "update_clean_bytes_tally"		"9876543210"
                "time_last_update_corruption"		"0"
                "apps"
                {
                    "228980"		"459081222"
                }
            }
            "1"
            {
                "path"		"D:\\SteamLibrary"
                "label"		""
                "contentid"		"987654321"
                "totalsize"		"2000396321792"
                "apps"
                {
                    "22320"		"2308740525"
                }
            }
        }
        """;

    private const string OldFormatLibraryFolders = """
        "LibraryFolders"
        {
            "TimeNextStatsReport"		"1623276754"
            "ContentStatsID"		"-8331543168035515255"
            "1"		"G:\\SteamLibrary"
            "2"		"A:\\SteamLibrary"
        }
        """;

    private const string AppManifest = """
        "AppState"
        {
            "appid"		"22320"
            "Universe"		"1"
            "name"		"The Elder Scrolls III: Morrowind"
            "StateFlags"		"4"
            "installdir"		"Morrowind"
            "LastUpdated"		"1584911469"
            "SizeOnDisk"		"2308740525"
        }
        """;

    [Fact]
    public void ParsesNewFormatLibraryFolders()
    {
        var root = VdfParser.Parse(NewFormatLibraryFolders);
        var folders = root.GetChild("libraryfolders");

        Assert.NotNull(folders);
        Assert.Equal(@"C:\Program Files (x86)\Steam", folders!.GetChild("0")!.GetValue("path"));
        Assert.Equal(@"D:\SteamLibrary", folders.GetChild("1")!.GetValue("path"));
        Assert.Equal("2308740525", folders.GetChild("1")!.GetChild("apps")!.GetValue("22320"));
    }

    [Fact]
    public void ParsesOldFormatLibraryFolders()
    {
        var root = VdfParser.Parse(OldFormatLibraryFolders);
        var folders = root.GetChild("LibraryFolders");

        Assert.NotNull(folders);
        Assert.Equal(@"G:\SteamLibrary", folders!.GetValue("1"));
        Assert.Equal(@"A:\SteamLibrary", folders.GetValue("2"));
        Assert.Equal("1623276754", folders.GetValue("TimeNextStatsReport"));
    }

    [Fact]
    public void ParsesAppManifest()
    {
        var root = VdfParser.Parse(AppManifest);
        var state = root.GetChild("AppState");

        Assert.NotNull(state);
        Assert.Equal("22320", state!.GetValue("appid"));
        Assert.Equal("Morrowind", state.GetValue("installdir"));
    }

    [Fact]
    public void KeysAreCaseInsensitive()
    {
        var root = VdfParser.Parse(AppManifest);
        Assert.NotNull(root.GetChild("appstate"));
        Assert.Equal("Morrowind", root.GetChild("APPSTATE")!.GetValue("INSTALLDIR"));
    }

    [Fact]
    public void HandlesCommentsAndEscapes()
    {
        var root = VdfParser.Parse("""
            // top comment
            "root"
            {
                // inner comment
                "key"	"line1\nline2 \"quoted\" back\\slash"
            }
            """);
        Assert.Equal("line1\nline2 \"quoted\" back\\slash", root.GetChild("root")!.GetValue("key"));
    }

    [Fact]
    public void ThrowsOnUnterminatedString() =>
        Assert.Throws<FormatException>(() => VdfParser.Parse("\"key\" \"unterminated"));
}
