using Mri.Curation.Parsing;

namespace Mri.Curation.Tests;

public class RowParserTests
{
    private const string SampleCsv =
        """
        MOD NAME,PAGE LINK,NEXUS DOWNLOAD LINK,VERSION,COMMENT
        BUG FIXES / PATCHES,,,,
        Patch for Purists,https://www.nexusmods.com/morrowind/mods/45096,https://www.nexusmods.com/morrowind/mods/45096?tab=files&file_id=1000019704,4.0.2,
        Expansion Delay,https://www.nexusmods.com/morrowind/mods/47588,,1.3,"Load before Patch for Purist, Version 1.3"
        ,,,,"orphan comment row"
        Some Direct Mod,https://www.dropbox.com/scl/fi/abc/Mod.7z?rlkey=x&dl=0,,,
        Delta Plugin,https://gitlab.com/bmwinger/delta-plugin/-/releases,,,Merge objects
        Patch for Purists,https://example.com/duplicate,,,
        """;

    [Fact]
    public void ClassifiesRowKinds()
    {
        var rows = RowParser.Parse(SampleCsv);

        Assert.Equal(RowKind.CategoryHeader, rows[0].Kind);
        Assert.Equal("Bug Fixes / Patches", rows[0].Category);
        Assert.Equal(RowKind.Mod, rows[1].Kind);
        Assert.Equal(RowKind.Note, rows[3].Kind);
    }

    [Fact]
    public void ModsInheritTheActiveCategory()
    {
        var rows = RowParser.Parse(SampleCsv);
        Assert.All(rows.Where(r => r.Kind == RowKind.Mod),
            r => Assert.Equal("Bug Fixes / Patches", r.Category));
    }

    [Fact]
    public void ExtractsNexusIds()
    {
        var pfp = RowParser.Parse(SampleCsv).First(r => r.Name == "Patch for Purists");

        Assert.Equal("nexus", pfp.Handler);
        Assert.Equal(45096, pfp.NexusId);
        Assert.Equal(1000019704, pfp.NexusFileId);
    }

    [Fact]
    public void ClassifiesHandlers()
    {
        var rows = RowParser.Parse(SampleCsv).Where(r => r.Kind == RowKind.Mod).ToList();

        Assert.Equal("direct", rows.First(r => r.Name == "Some Direct Mod").Handler);
        Assert.Equal("github", rows.First(r => r.Name == "Delta Plugin").Handler);
    }

    [Fact]
    public void DuplicateNamesGetUniqueSlugs()
    {
        var slugs = RowParser.Parse(SampleCsv)
            .Where(r => r.Name == "Patch for Purists")
            .Select(r => r.Slug)
            .ToList();

        Assert.Equal(["patch-for-purists", "patch-for-purists-2"], slugs);
    }

    [Fact]
    public void QuotedCommasSurviveCsvParsing()
    {
        var expansionDelay = RowParser.Parse(SampleCsv).First(r => r.Name == "Expansion Delay");
        Assert.Equal("Load before Patch for Purist, Version 1.3", expansionDelay.Comment);
    }

    [Fact]
    public void RealSpreadsheetSnapshotParses()
    {
        var csvPath = FindRepoFile("data/modlist.source.csv");
        var rows = RowParser.Parse(File.ReadAllText(csvPath));

        // 616 sheet rows + the MRI DEPENDENCY ADDITIONS section (Mercy CAO,
        // Tyddy UHQ, and the 8 key-batch dependency/successor rows).
        Assert.Equal(627, rows.Count(r => r.Kind == RowKind.Mod));
        // 24 sheet categories + the appended MRI DEPENDENCY ADDITIONS section.
        Assert.Equal(25, rows.Count(r => r.Kind == RowKind.CategoryHeader));
        // 563 sheet nexus rows + 8 nexus additions + Khajiit's row moved to
        // its live Nexus mirror (sheet column C).
        Assert.Equal(572, rows.Count(r => r is { Kind: RowKind.Mod, Handler: "nexus" }));
        // Every mod row must end up with a unique slug.
        var slugs = rows.Where(r => r.Kind == RowKind.Mod).Select(r => r.Slug).ToList();
        Assert.Equal(slugs.Count, slugs.Distinct().Count());
    }

    private static string FindRepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, relative)))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return Path.Combine(dir!, relative);
    }
}
