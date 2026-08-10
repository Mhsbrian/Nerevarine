using Mri.Core.Umo;

namespace Mri.Core.Tests.Umo;

public class UmoProgressParserTests
{
    [Theory]
    [InlineData("Downloading Patch for Purists ...", UmoEventKind.Download, "Patch for Purists")]
    [InlineData("downloading archive.7z", UmoEventKind.Download, "archive.7z")]
    [InlineData("Extracting Aesthesia Groundcover ...", UmoEventKind.Extract, "Aesthesia Groundcover")]
    [InlineData("ERROR: Weapon Sheathing", UmoEventKind.ModFailed, "Weapon Sheathing")]
    [InlineData("download failed: Some Mod Name", UmoEventKind.ModFailed, "Some Mod Name")]
    public void ClassifiesKnownShapes(string line, UmoEventKind kind, string modName)
    {
        var evt = UmoProgressParser.Parse(line);
        Assert.Equal(kind, evt.Kind);
        Assert.Equal(modName, evt.ModName);
    }

    [Fact]
    public void ExtractsCounters()
    {
        var evt = UmoProgressParser.Parse("Downloading Patch for Purists (3/617)");
        Assert.Equal(UmoEventKind.Download, evt.Kind);
        Assert.Equal(3, evt.Current);
        Assert.Equal(617, evt.Total);
    }

    [Fact]
    public void BareCounterIsProgress()
    {
        var evt = UmoProgressParser.Parse("[42/617] syncing");
        Assert.Equal(UmoEventKind.Progress, evt.Kind);
        Assert.Equal(42, evt.Current);
        Assert.Equal(617, evt.Total);
    }

    [Fact]
    public void UnknownLinesDegradeToInfoWithRawText()
    {
        var evt = UmoProgressParser.Parse("some future umo output we have never seen");
        Assert.Equal(UmoEventKind.Info, evt.Kind);
        Assert.Equal("some future umo output we have never seen", evt.RawLine);
    }

    [Fact]
    public void EmptyLineIsInfo() =>
        Assert.Equal(UmoEventKind.Info, UmoProgressParser.Parse("").Kind);
}
