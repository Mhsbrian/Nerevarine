using Mri.Core.Modlist;
using Mri.Core.OpenMw;

namespace Mri.Core.Tests.OpenMw;

public class OpenMwCfgComposerTests
{
    private static CfgComposition Sample(bool includeDelta = false) => new()
    {
        GameDataFilesDir = @"C:\Games\Steam\steamapps\common\Morrowind\Data Files",
        ModsRootDir = @"C:\MorrowindRemake\mods",
        FallbackLines = ["FontColor_color_normal,202,165,96", "Weather_Clear_Cloud_Texture,Tx_Sky_Clear"],
        Plan = new LoadOrderPlan
        {
            DataDirs = includeDelta
                ? ["PatchForPurists", "Aesthesia/00 Core", "delta-merged"]
                : ["PatchForPurists", "Aesthesia/00 Core"],
            ContentFiles = includeDelta
                ? ["Patch for Purists.esm", "delta-merged.omwaddon"]
                : ["Patch for Purists.esm", "Merge Input.esp"],
            GroundcoverFiles = ["Grass_AC.esp"],
            FallbackArchives = ["SomeMod.bsa"],
        },
        AppVersion = "0.1.0",
        ListVersion = "2026.08.0",
    };

    [Fact]
    public void ComposedCfgHasExpectedStructureAndOrder()
    {
        var text = OpenMwCfgComposer.Compose(Sample());
        var lines = text.Split('\n');

        Assert.StartsWith(OpenMwCfgComposer.HeaderPrefix, lines[0]);
        Assert.Contains("list 2026.08.0", lines[0]);

        var cfg = OpenMwCfg.Parse(text);
        Assert.Equal(
            ["Morrowind.bsa", "Tribunal.bsa", "Bloodmoon.bsa", "SomeMod.bsa"],
            cfg.GetValues("fallback-archive"));
        Assert.Equal(
            [
                "\"C:\\Games\\Steam\\steamapps\\common\\Morrowind\\Data Files\"",
                "\"C:\\MorrowindRemake\\mods\\PatchForPurists\"",
                "\"C:\\MorrowindRemake\\mods\\Aesthesia\\00 Core\"",
            ],
            cfg.GetValues("data").Select(NormalizeSlashes).ToList());
        Assert.Equal(
            ["Morrowind.esm", "Tribunal.esm", "Bloodmoon.esm", "Patch for Purists.esm", "Merge Input.esp"],
            cfg.GetValues("content"));
        Assert.Equal(["Grass_AC.esp"], cfg.GetValues("groundcover"));
        Assert.Equal(["win1252"], cfg.GetValues("encoding"));
        Assert.Equal(2, cfg.GetValues("fallback").Count);
    }

    // Path.Combine uses '/' on Linux dev machines; the assertions target the
    // Windows shape.
    private static string NormalizeSlashes(string s) => s.Replace('/', '\\');

    [Fact]
    public void MissingExpansionsDropTheirLines()
    {
        var text = OpenMwCfgComposer.Compose(Sample() with { HasTribunal = false, HasBloodmoon = false });
        var cfg = OpenMwCfg.Parse(text);

        Assert.DoesNotContain("Tribunal.esm", cfg.GetValues("content"));
        Assert.DoesNotContain("Bloodmoon.esm", cfg.GetValues("content"));
        Assert.DoesNotContain("Tribunal.bsa", cfg.GetValues("fallback-archive"));
    }

    [Fact]
    public void ComposeIsDeterministic() =>
        Assert.Equal(OpenMwCfgComposer.Compose(Sample()), OpenMwCfgComposer.Compose(Sample()));

    [Fact]
    public void IsCurrentDetectsMatchAndDrift()
    {
        var composition = Sample();
        var text = OpenMwCfgComposer.Compose(composition);

        Assert.True(OpenMwCfgComposer.IsCurrent(text, composition));
        Assert.False(OpenMwCfgComposer.IsCurrent(text, Sample(includeDelta: true)));
        Assert.False(OpenMwCfgComposer.IsCurrent(text + "data=\"C:\\extra\"\n", composition));
    }

    [Theory]
    [InlineData(@"C:\Mods\Simple", "\"C:\\Mods\\Simple\"")]
    [InlineData(@"C:\Mods\With Spaces\dir", "\"C:\\Mods\\With Spaces\\dir\"")]
    [InlineData(@"C:\Mods\Fish & Chips", "\"C:\\Mods\\Fish && Chips\"")]
    [InlineData("C:\\Mods\\He said \"hi\"", "\"C:\\Mods\\He said &\"hi&\"\"")]
    [InlineData(@"C:\Mods\Ünïcode\日本語", "\"C:\\Mods\\Ünïcode\\日本語\"")]
    public void PathQuotingEscapesOpenMwSpecials(string input, string expected) =>
        Assert.Equal(expected, OpenMwCfgWriter.QuotePath(input));
}
