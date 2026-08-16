using Mri.Core.OpenMw;

namespace Mri.Core.Tests.OpenMw;

public class QualityPresetsTests
{
    [Fact]
    public void MasterMatchesShippedTemplateValues()
    {
        var o = QualityPresets.Overrides(QualityTier.Master).ToDictionary(x => (x.Section, x.Key), x => x.Value);
        Assert.Equal("81920", o[("Camera", "viewing distance")]);
        Assert.Equal("32", o[("Shaders", "max lights")]);
        Assert.Equal("3", o[("Water", "reflection detail")]);
    }

    [Fact]
    public void ApplyOverwritesOnlyItsKeysAndPreservesOthers()
    {
        var cfg = "[Camera]\nviewing distance = 81920\nfield of view = 60\n[Shadows]\nenable shadows = true\n";
        var text = QualityPresets.Apply(cfg, QualityTier.Apprentice);
        Assert.Contains("viewing distance = 24576", text);
        Assert.Contains("field of view = 60", text);          // untouched key survives
        Assert.Contains("enable shadows = false", text);
    }

    [Fact]
    public void ApplyIsIdempotentPerTier()
    {
        var once = QualityPresets.Apply("", QualityTier.Adept);
        Assert.Equal(once, QualityPresets.Apply(once, QualityTier.Adept));
    }

    [Theory]
    [InlineData(8L << 30, 32L << 30, 16, QualityTier.Master)]    // strong GPU
    [InlineData(4L << 30, 16L << 30, 8, QualityTier.Adept)]      // mid GPU
    [InlineData(1L << 30, 8L << 30, 4, QualityTier.Apprentice)]  // weak GPU
    [InlineData(0L, 32L << 30, 16, QualityTier.Master)]          // no VRAM info, big box
    [InlineData(0L, 8L << 30, 2, QualityTier.Apprentice)]        // no VRAM info, small box
    public void DetectMapsHardwareToTier(long vram, long ram, int cores, QualityTier expected) =>
        Assert.Equal(expected, QualityPresets.Detect(vram, ram, cores));
}
