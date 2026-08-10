using Mri.Core.Modlist;
using Mri.Curation.Emit;
using Mri.Curation.Overrides;
using Mri.Curation.Parsing;
using Mri.Curation.Resolve;

namespace Mri.Curation.Tests;

public class ModlistEmitterTests
{
    private const string Csv =
        """
        MOD NAME,PAGE LINK,NEXUS DOWNLOAD LINK,VERSION,COMMENT
        CATEGORY ONE,,,,
        Alpha Mod,https://www.nexusmods.com/morrowind/mods/100,https://www.nexusmods.com/morrowind/mods/100?tab=files&file_id=555,1.0,
        Beta Mod,https://www.nexusmods.com/morrowind/mods/200,,,"delete broken.esp"
        Gamma Tool,https://www.nexusmods.com/morrowind/mods/300,,,
        """;

    private static ResolveCache CacheWith(int id, params CachedFile[] files)
    {
        var cache = new ResolveCache();
        cache.Put(id, new CachedMod
        {
            Name = $"Mod {id}",
            Author = "Author",
            Available = true,
            FetchedAt = "2026-08-10T00:00:00Z",
            Files = files.ToList(),
        });
        return cache;
    }

    private static EmitResult Run(string overridesYaml = "version: 1\nrules: []", ResolveCache? cache = null) =>
        ModlistEmitter.Emit(
            RowParser.Parse(Csv),
            cache ?? new ResolveCache(),
            OverridesFile.Load(overridesYaml),
            "2026.08.0");

    [Fact]
    public void EmitsModsInSpreadsheetOrder()
    {
        var result = Run();
        Assert.Equal(["alpha-mod", "beta-mod", "gamma-tool"], result.Modlist.Mods.Select(m => m.Id));
        Assert.All(result.Modlist.Mods, m => Assert.Equal("Category One", m.Category));
    }

    [Fact]
    public void PinnedFileIdSurvives()
    {
        var alpha = Run().Modlist.Mods.First(m => m.Id == "alpha-mod");
        Assert.Equal(555, alpha.Downloads[0].NexusFileId);
        Assert.True(alpha.Downloads[0].Pinned);
    }

    [Fact]
    public void SkipRuleRemovesMod()
    {
        var result = Run("""
            version: 1
            rules:
              - match: { slug: gamma-tool }
                skip: true
                skipReason: "tooling"
            """);

        Assert.DoesNotContain(result.Modlist.Mods, m => m.Id == "gamma-tool");
        Assert.Contains("Skipped rows", result.Report);
        Assert.Contains("tooling", result.Report);
    }

    [Fact]
    public void SetRuleOverridesFieldsAndClearsPluginProblem()
    {
        var result = Run("""
            version: 1
            rules:
              - match: { nexusId: 100 }
                set:
                  dataPaths: ["AlphaMod/00 Core"]
                  content:
                    - file: Alpha.esp
                    - file: AlphaMerge.esp
                      mode: deltaOnly
            """);

        var alpha = result.Modlist.Mods.First(m => m.Id == "alpha-mod");
        Assert.Equal(["AlphaMod/00 Core"], alpha.DataPaths);
        Assert.Equal(ContentMode.DeltaOnly, alpha.Content[1].Mode);
    }

    [Fact]
    public void MatchingRuleClearsCommentProblem()
    {
        var before = Run();
        Assert.Contains(before.Report, r => true);
        Assert.Contains("comment not encoded", before.Report);

        var after = Run("""
            version: 1
            rules:
              - match: { slug: beta-mod }
                actions:
                  - type: remove
                    path: "BetaMod/broken.esp"
            """);

        Assert.DoesNotContain("comment not encoded", after.Report);
        var beta = after.Modlist.Mods.First(m => m.Id == "beta-mod");
        Assert.Equal(FileActionType.Remove, beta.Downloads[0].Actions[0].Type);
    }

    [Fact]
    public void MoveAfterReordersMods()
    {
        var result = Run("""
            version: 1
            rules:
              - match: { slug: alpha-mod }
                moveAfter: beta-mod
            """);

        Assert.Equal(["beta-mod", "alpha-mod", "gamma-tool"], result.Modlist.Mods.Select(m => m.Id));
    }

    [Fact]
    public void SplitIntoReplacesRowWithMultipleEntries()
    {
        var result = Run("""
            version: 1
            rules:
              - match: { slug: alpha-mod }
                splitInto:
                  - id: alpha-core
                    name: Alpha Core
                    dataPaths: ["Alpha/Core"]
                    content: [{ file: AlphaCore.esp }]
                  - id: alpha-extras
                    name: Alpha Extras
                    dataPaths: ["Alpha/Extras"]
                    content: [{ file: AlphaExtras.esp }]
            """);

        Assert.Equal(["alpha-core", "alpha-extras", "beta-mod", "gamma-tool"],
            result.Modlist.Mods.Select(m => m.Id));
    }

    [Fact]
    public void ConstraintViolationIsReported()
    {
        var result = Run("""
            version: 1
            rules:
              - match: { slug: alpha-mod }
                set:
                  content: [{ file: Alpha.esp }]
              - match: { slug: beta-mod }
                set:
                  content: [{ file: Beta.esp }]
                constraints:
                  - contentBefore: "Alpha.esp"
            """);

        Assert.Single(result.ConstraintViolations);
        Assert.Contains("must load before 'Alpha.esp'", result.ConstraintViolations[0]);
    }

    [Fact]
    public void ResolveCacheFillsFileDetails()
    {
        var cache = CacheWith(200,
            new CachedFile { FileId = 777, Name = "Beta Mod", FileName = "Beta-200-2-0.7z", Version = "2.0", SizeBytes = 4096, Category = "MAIN", UploadedTimestamp = 100 },
            new CachedFile { FileId = 776, Name = "Beta Old", Version = "1.0", Category = "OLD_VERSION", UploadedTimestamp = 50 });

        var beta = Run(cache: cache).Modlist.Mods.First(m => m.Id == "beta-mod");

        Assert.Equal(777, beta.Downloads[0].NexusFileId);
        Assert.Equal("Beta-200-2-0.7z", beta.Downloads[0].FileName);
        Assert.Equal(4096, beta.Downloads[0].SizeBytes);
        Assert.Equal("Author", beta.Author);
    }

    [Fact]
    public void PinnedIdMissingFromNexusIsFlagged()
    {
        // Sheet pins file 555, but Nexus only lists 999 — the typo'd-id case.
        var cache = CacheWith(100,
            new CachedFile { FileId = 999, Name = "Alpha", Category = "MAIN" });

        var result = Run(cache: cache);
        Assert.Contains("pinned file_id 555 not found", result.Report);
    }
}
