using Mri.Core.OpenMw;

namespace Mri.Core.Tests.OpenMw;

public class SettingsCfgMergerTests
{
    private const string UserSettings = """
        [Camera]
        viewing distance = 6144

        [GUI]
        scaling factor = 1.25

        [Input]
        invert y axis = true
        """;

    private const string Template = """
        # tuned template
        [Camera]
        viewing distance = 81920
        reverse z = true

        [Shadows]
        enable shadows = true
        """;

    [Fact]
    public void TemplateKeysOverlayUserKeysSurvive()
    {
        var result = SettingsCfgMerger.Merge(UserSettings, Template);
        var doc = IniDocument.Parse(result.Text);

        Assert.Equal("81920", doc.Get("Camera", "viewing distance"));
        Assert.Equal("true", doc.Get("Camera", "reverse z"));
        Assert.Equal("true", doc.Get("Shadows", "enable shadows"));
        // The user's own settings are untouched.
        Assert.Equal("1.25", doc.Get("GUI", "scaling factor"));
        Assert.Equal("true", doc.Get("Input", "invert y axis"));
        Assert.Equal(3, result.ChangedKeys);
    }

    [Fact]
    public void MergeIsIdempotent()
    {
        var once = SettingsCfgMerger.Merge(UserSettings, Template);
        var twice = SettingsCfgMerger.Merge(once.Text, Template);

        Assert.Equal(once.Text, twice.Text);
        Assert.Equal(0, twice.ChangedKeys);
    }

    [Fact]
    public void IsAppliedReflectsMergeState()
    {
        Assert.False(SettingsCfgMerger.IsApplied(UserSettings, Template));
        var merged = SettingsCfgMerger.Merge(UserSettings, Template);
        Assert.True(SettingsCfgMerger.IsApplied(merged.Text, Template));
    }

    [Fact]
    public void MergeIntoEmptyFileProducesTemplateContent()
    {
        var result = SettingsCfgMerger.Merge("", Template);
        var doc = IniDocument.Parse(result.Text);

        Assert.Equal("81920", doc.Get("Camera", "viewing distance"));
        Assert.Equal("true", doc.Get("Shadows", "enable shadows"));
    }
}
