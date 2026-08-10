using System.IO.Compression;
using Mri.Core.Logging;

namespace Mri.Core.Tests.Logging;

public class DiagnosticsBundlerTests : IDisposable
{
    private readonly string _root;
    private readonly string _installDir;
    private readonly string _openMwDir;
    private const string Secret = "nexus-key-abcdef123456";

    public DiagnosticsBundlerTests()
    {
        _root = Directory.CreateTempSubdirectory("mri-bundle-test-").FullName;
        _installDir = Path.Combine(_root, "install");
        _openMwDir = Path.Combine(_root, "openmw-config");

        Directory.CreateDirectory(Path.Combine(_installDir, "logs"));
        File.WriteAllText(Path.Combine(_installDir, "logs", "installer-1.log"), "log one");
        File.WriteAllText(Path.Combine(_installDir, "logs", "installer-2.log"), "log two");
        File.WriteAllText(Path.Combine(_installDir, "state.json"), """{"schemaVersion":1}""");
        Directory.CreateDirectory(Path.Combine(_installDir, "umo-conf"));
        File.WriteAllText(Path.Combine(_installDir, "umo-conf", "config.json"),
            $$"""{"apikey": "{{Secret}}", "base_path": "C:\\mods"}""");
        Directory.CreateDirectory(Path.Combine(_installDir, "modlist"));
        File.WriteAllText(Path.Combine(_installDir, "modlist", "morrowind-remake.json"), "[]");
        Directory.CreateDirectory(Path.Combine(_installDir, "tools", "openmw"));
        File.WriteAllText(Path.Combine(_installDir, "tools", "openmw", ".mri-tool.json"),
            """{"id":"openmw","version":"0.51.0"}""");
        Directory.CreateDirectory(_openMwDir);
        File.WriteAllText(Path.Combine(_openMwDir, "openmw.cfg"), "data=\"C:\\mods\\A\"");
        File.WriteAllText(Path.Combine(_openMwDir, "settings.cfg"), "[Camera]\nviewing distance = 49152");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void BundlesEverythingWithSecretsRedacted()
    {
        var zipPath = DiagnosticsBundler.CreateZip(_installDir, _openMwDir, [Secret]);

        Assert.True(File.Exists(zipPath));
        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("logs/installer-1.log", names);
        Assert.Contains("logs/installer-2.log", names);
        Assert.Contains("state.json", names);
        Assert.Contains("umo-conf/config.json", names);
        Assert.Contains("modlist/morrowind-remake.json", names);
        Assert.Contains("openmw-config/openmw.cfg", names);
        Assert.Contains("openmw-config/settings.cfg", names);
        Assert.Contains("tool-markers/openmw/.mri-tool.json", names);
        Assert.Contains("environment.txt", names);

        // The API key must not exist anywhere in the bundle.
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            var content = reader.ReadToEnd();
            Assert.DoesNotContain(Secret, content);
        }

        // But the redacted umo config still shows its structure.
        var umoConfig = zip.GetEntry("umo-conf/config.json")!;
        using var umoReader = new StreamReader(umoConfig.Open());
        var umoText = umoReader.ReadToEnd();
        Assert.Contains("«redacted»", umoText);
        Assert.Contains("base_path", umoText);
    }

    [Fact]
    public void MissingPiecesAreSkippedGracefully()
    {
        var bareInstall = Path.Combine(_root, "bare");
        Directory.CreateDirectory(bareInstall);

        var zipPath = DiagnosticsBundler.CreateZip(bareInstall, Path.Combine(_root, "nope"), []);

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, e => e.FullName == "environment.txt");
    }

    [Fact]
    public void ManifestListsIncludedFiles()
    {
        var zipPath = DiagnosticsBundler.CreateZip(_installDir, _openMwDir, [Secret]);

        using var zip = ZipFile.OpenRead(zipPath);
        using var reader = new StreamReader(zip.GetEntry("environment.txt")!.Open());
        var env = reader.ReadToEnd();

        Assert.Contains("included: logs/installer-1.log", env);
        Assert.Contains("included: state.json", env);
        Assert.Contains("os:", env);
    }
}
