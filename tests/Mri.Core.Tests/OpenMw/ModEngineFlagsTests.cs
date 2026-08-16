using Mri.Core.OpenMw;

namespace Mri.Core.Tests.OpenMw;

public class ModEngineFlagsTests
{
    [Fact]
    public void ReadFallsBackToModDefaultWhenAbsent()
    {
        foreach (var flag in ModEngineFlags.All)
            Assert.Equal(flag.ModDefault, ModEngineFlags.Read("", flag));
    }

    [Fact]
    public void WriteThenReadRoundTrips()
    {
        var flag = ModEngineFlags.All[0];
        var off = ModEngineFlags.Write("", flag, false);
        Assert.False(ModEngineFlags.Read(off, flag));
        Assert.True(ModEngineFlags.Read(ModEngineFlags.Write(off, flag, true), flag));
    }

    [Fact]
    public void CatalogMatchesShippedTemplateDefaults()
    {
        var template = File.ReadAllText(Path.Combine(FindRepoRoot(), "data/templates/settings.template.cfg"));
        foreach (var flag in ModEngineFlags.All)
            Assert.Equal(flag.ModDefault, ModEngineFlags.Read(template, flag));
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "data/templates/settings.template.cfg")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
