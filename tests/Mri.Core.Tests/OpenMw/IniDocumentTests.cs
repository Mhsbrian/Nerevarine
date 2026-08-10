using Mri.Core.OpenMw;

namespace Mri.Core.Tests.OpenMw;

public class IniDocumentTests
{
    private const string Sample = """
        # OpenMW user settings

        [Camera]
        viewing distance = 6144
        field of view = 60

        [Shadows]
        enable shadows = false
        """;

    [Fact]
    public void GetReadsExistingValues()
    {
        var doc = IniDocument.Parse(Sample);
        Assert.Equal("6144", doc.Get("Camera", "viewing distance"));
        Assert.Equal("false", doc.Get("Shadows", "enable shadows"));
        Assert.Null(doc.Get("Camera", "nonexistent"));
        Assert.Null(doc.Get("Nonexistent", "key"));
    }

    [Fact]
    public void SetUpdatesInPlacePreservingLayout()
    {
        var doc = IniDocument.Parse(Sample);
        Assert.True(doc.Set("Camera", "viewing distance", "81920"));

        var text = doc.ToText();
        Assert.Contains("viewing distance = 81920", text);
        Assert.Contains("# OpenMW user settings", text);
        // Untouched neighbors keep their exact form.
        Assert.Contains("field of view = 60", text);
    }

    [Fact]
    public void SetSameValueReportsNoChange()
    {
        var doc = IniDocument.Parse(Sample);
        Assert.False(doc.Set("Camera", "viewing distance", "6144"));
    }

    [Fact]
    public void SetInsertsMissingKeyIntoItsSection()
    {
        var doc = IniDocument.Parse(Sample);
        Assert.True(doc.Set("Camera", "reverse z", "true"));

        var lines = doc.ToText().Split('\n');
        var cameraIdx = Array.IndexOf(lines, "[Camera]");
        var shadowsIdx = Array.IndexOf(lines, "[Shadows]");
        var newIdx = Array.IndexOf(lines, "reverse z = true");
        Assert.InRange(newIdx, cameraIdx + 1, shadowsIdx - 1);
    }

    [Fact]
    public void SetAppendsMissingSectionAtEnd()
    {
        var doc = IniDocument.Parse(Sample);
        Assert.True(doc.Set("Groundcover", "enabled", "true"));

        var text = doc.ToText();
        Assert.Contains("[Groundcover]\nenabled = true", text.Replace("\r", ""));
    }

    [Fact]
    public void SectionAndKeyLookupIsCaseInsensitive()
    {
        var doc = IniDocument.Parse(Sample);
        Assert.Equal("6144", doc.Get("camera", "VIEWING DISTANCE"));
    }

    [Fact]
    public void EntriesEnumeratesAll()
    {
        var entries = IniDocument.Parse(Sample).Entries().ToList();
        Assert.Equal(3, entries.Count);
        Assert.Contains(("Camera", "field of view", "60"), entries);
    }

    [Fact]
    public void EmptyDocumentGrowsCorrectly()
    {
        var doc = IniDocument.Parse("");
        doc.Set("Video", "resolution x", "2560");
        doc.Set("Video", "resolution y", "1440");

        Assert.Equal("[Video]\nresolution x = 2560\nresolution y = 1440\n", doc.ToText());
    }
}
