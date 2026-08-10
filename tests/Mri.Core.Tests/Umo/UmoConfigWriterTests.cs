using System.Text.Json;
using Mri.Core.Umo;

namespace Mri.Core.Tests.Umo;

public class UmoConfigWriterTests : IDisposable
{
    private readonly string _dir;

    public UmoConfigWriterTests() =>
        _dir = Directory.CreateTempSubdirectory("mri-umocfg-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private UmoSettings Settings() => new()
    {
        ModBaseDir = Path.Combine(_dir, "mods"),
        CacheDir = Path.Combine(_dir, "downloads"),
        Tes3cmdPath = Path.Combine(_dir, "tools", "tes3cmd.exe"),
    };

    [Fact]
    public void WritesUmoNativeSchema()
    {
        var writer = new UmoConfigWriter(Path.Combine(_dir, "conf"));
        writer.Write(Settings());

        using var doc = JsonDocument.Parse(File.ReadAllText(writer.ConfigPath));
        var root = doc.RootElement;

        // Exactly the keys umo's load_config() requires (uppercase — a
        // lowercase schema produced a KeyError: 'NEXUS_API_KEY' in the field).
        Assert.Equal(Path.GetFullPath(Path.Combine(_dir, "mods")), root.GetProperty("BASEPATH").GetString());
        Assert.Equal(Path.GetFullPath(Path.Combine(_dir, "downloads")), root.GetProperty("CACHE_DIR").GetString());
        Assert.Equal(Path.GetFullPath(Path.Combine(_dir, "tools", "tes3cmd.exe")), root.GetProperty("TES3CMD").GetString());

        // The key must be present (required) but EMPTY — the real key travels
        // via the UMO_NEXUS_API_KEY environment override and never rests on disk.
        Assert.Equal("", root.GetProperty("NEXUS_API_KEY").GetString());
    }

    [Fact]
    public void IsCurrentTrueAfterWriteFalseWhenSettingsChange()
    {
        var writer = new UmoConfigWriter(Path.Combine(_dir, "conf"));
        writer.Write(Settings());

        Assert.True(writer.IsCurrent(Settings()));
        Assert.False(writer.IsCurrent(Settings() with { CacheDir = Path.Combine(_dir, "elsewhere") }));
    }

    [Fact]
    public void LegacyLowercaseConfigIsDetectedAsStale()
    {
        // What builds before the schema fix wrote — must be regenerated, not
        // trusted for merely existing.
        var confDir = Path.Combine(_dir, "conf");
        Directory.CreateDirectory(confDir);
        File.WriteAllText(Path.Combine(confDir, "config.json"),
            """{"apikey": "k", "base_path": "C:\\mods", "cache_path": "C:\\dl", "tes3cmd": null}""");

        var writer = new UmoConfigWriter(confDir);
        Assert.False(writer.IsCurrent(Settings()));

        writer.Write(Settings());
        Assert.True(writer.IsCurrent(Settings()));
    }
}
