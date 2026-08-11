using Mri.Core.Modlist;
using Mri.Curation.Inspect;

namespace Mri.Curation.Tests;

public class LayoutInspectorTests : IDisposable
{
    private readonly string _root;

    public LayoutInspectorTests() =>
        _root = Directory.CreateTempSubdirectory("mri-layout-test-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ModEntry Mod(string id, string extractTo, string category = "Test Cat") => new()
    {
        Id = id,
        Name = id,
        Category = category,
        Source = new ModSource { Handler = ModHandler.Direct, Url = "https://x" },
        Downloads = [new ModDownload { FileName = id, ExtractTo = extractTo }],
        DataPaths = [extractTo],
    };

    private string ModDir(string extractTo) =>
        Path.Combine(_root, ModlistCompiler.CategoryDir("Test Cat"), extractTo);

    [Fact]
    public void RootLevelDataIsOk()
    {
        Directory.CreateDirectory(Path.Combine(ModDir("RootMod"), "Textures"));
        var result = LayoutInspector.Inspect(_root, Mod("root-mod", "RootMod"));
        Assert.Equal("ok", result.Verdict);
    }

    [Fact]
    public void NestedDataFilesIsAutoFixed()
    {
        // The field case: Westly's textures under "<wrapper>/Data Files/".
        Directory.CreateDirectory(Path.Combine(
            ModDir("Westly"), "Westly's Head Pack_Complete", "Data Files", "Textures"));

        var result = LayoutInspector.Inspect(_root, Mod("westly", "Westly"));

        Assert.Equal("auto", result.Verdict);
        Assert.Equal(["Westly/Westly's Head Pack_Complete/Data Files"], result.ChosenDataPaths);
    }

    [Fact]
    public void SingleWrapperIsAutoFixed()
    {
        Directory.CreateDirectory(Path.Combine(ModDir("Wrapped"), "The Actual Mod", "meshes"));
        var result = LayoutInspector.Inspect(_root, Mod("wrapped", "Wrapped"));

        Assert.Equal("auto", result.Verdict);
        Assert.Equal(["Wrapped/The Actual Mod"], result.ChosenDataPaths);
    }

    [Fact]
    public void BainCoreVariantIsPickedOthersListed()
    {
        Directory.CreateDirectory(Path.Combine(ModDir("Bain"), "00 Core", "meshes"));
        Directory.CreateDirectory(Path.Combine(ModDir("Bain"), "01 Options", "meshes"));

        var result = LayoutInspector.Inspect(_root, Mod("bain", "Bain"));

        Assert.Equal("core-picked", result.Verdict);
        Assert.Equal(["Bain/00 Core"], result.ChosenDataPaths);
        Assert.Contains("Bain/01 Options", result.OtherCandidates);
    }

    [Fact]
    public void AmbiguousVariantsAreFlaggedNotGuessed()
    {
        Directory.CreateDirectory(Path.Combine(ModDir("Choice"), "English Version", "meshes"));
        Directory.CreateDirectory(Path.Combine(ModDir("Choice"), "Russian Version", "meshes"));

        var result = LayoutInspector.Inspect(_root, Mod("choice", "Choice"));

        Assert.Equal("flagged", result.Verdict);
        Assert.Empty(result.ChosenDataPaths);
        Assert.Equal(2, result.OtherCandidates.Count);
    }

    [Fact]
    public void GeneratedYamlParsesAsOverridesAndTargetsTheRightMods()
    {
        Directory.CreateDirectory(Path.Combine(ModDir("RootMod"), "Textures"));
        Directory.CreateDirectory(Path.Combine(ModDir("Wrapped"), "Inner Mod", "icons"));

        var modlist = new Modlist
        {
            ListVersion = "t", Name = "test",
            Mods = [Mod("root-mod", "RootMod"), Mod("wrapped", "Wrapped")],
        };

        var (yaml, counts) = LayoutInspector.GenerateOverrides(_root, modlist);
        var parsed = Mri.Curation.Overrides.OverridesFile.Load(yaml);

        Assert.Equal(1, counts["ok"]);
        Assert.Equal(1, counts["auto"]);
        // Only the wrapped mod needs a rule; ok mods are left alone.
        var rule = Assert.Single(parsed.Rules);
        Assert.Equal("wrapped", rule.Match.Slug);
        Assert.Equal(["Wrapped/Inner Mod"], rule.Set!.DataPaths);
    }
}
