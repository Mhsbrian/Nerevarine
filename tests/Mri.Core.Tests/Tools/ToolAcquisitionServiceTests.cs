using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Mri.Core.IO;
using Mri.Core.Tools;

namespace Mri.Core.Tests.Tools;

public class ToolAcquisitionServiceTests : IDisposable
{
    private readonly string _root;
    private readonly byte[] _zipBytes;
    private readonly string _zipSha256;
    private int _downloadCount;

    public ToolAcquisitionServiceTests()
    {
        _root = Directory.CreateTempSubdirectory("mri-tools-test-").FullName;

        // Build an in-memory zip containing the probe exe.
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("bin/fake-tool.exe").Open();
            entry.Write("MZ fake"u8);
        }
        _zipBytes = buffer.ToArray();
        _zipSha256 = Convert.ToHexString(SHA256.HashData(_zipBytes));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private ToolAcquisitionService MakeService() =>
        new(
            new HttpClient(new StubHandler(_ =>
            {
                Interlocked.Increment(ref _downloadCount);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_zipBytes),
                };
            })),
            new ProcessRunner(),
            Path.Combine(_root, "tools"));

    private ToolSpec Spec(string? sha256 = null) => new()
    {
        Id = "fake-tool",
        Version = "1.0",
        InstallSubdir = "fake-tool",
        Platforms = new Dictionary<string, ToolVariant>
        {
            [ToolManifest.CurrentRid] = new()
            {
                Url = "https://example.com/fake-tool.zip",
                Sha256 = sha256,
                ArchiveType = ToolArchiveType.Zip,
                ExeProbe = "fake-tool.exe",
            },
        },
    };

    [Fact]
    public async Task DownloadsExtractsAndProbes()
    {
        var service = MakeService();
        await service.EnsureToolAsync(Spec(_zipSha256));

        Assert.True(service.IsInstalled(Spec(_zipSha256)));
        var exe = service.FindExe(Spec(_zipSha256));
        Assert.NotNull(exe);
        Assert.EndsWith(Path.Combine("bin", "fake-tool.exe"), exe);
    }

    [Fact]
    public async Task SecondEnsureIsANoOp()
    {
        var service = MakeService();
        await service.EnsureToolAsync(Spec());
        await service.EnsureToolAsync(Spec());

        Assert.Equal(1, _downloadCount);
    }

    [Fact]
    public async Task QuarantinedExeTriggersReextractionWithoutRedownload()
    {
        var service = MakeService();
        var spec = Spec();
        await service.EnsureToolAsync(spec);

        // Simulate the AV eating the exe: marker intact, binary gone.
        File.Delete(service.FindExe(spec)!);
        Assert.False(service.IsInstalled(spec));

        await service.EnsureToolAsync(spec);
        Assert.True(service.IsInstalled(spec));
        Assert.Equal(1, _downloadCount); // cached archive reused
    }

    [Fact]
    public async Task VersionBumpReinstalls()
    {
        var service = MakeService();
        await service.EnsureToolAsync(Spec());

        var upgraded = Spec() with { Version = "2.0" };
        Assert.False(service.IsInstalled(upgraded));
        await service.EnsureToolAsync(upgraded);
        Assert.True(service.IsInstalled(upgraded));
    }

    [Fact]
    public async Task WrongHashRefusesInstall()
    {
        var service = MakeService();
        var poisoned = Spec(new string('0', 64));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.EnsureToolAsync(poisoned));
        Assert.False(service.IsInstalled(poisoned));
    }

    private ToolSpec NsisSpec() => new()
    {
        Id = "openmw",
        Version = "0.51.0",
        InstallSubdir = "openmw",
        Platforms = new Dictionary<string, ToolVariant>
        {
            [ToolManifest.CurrentRid] = new()
            {
                Url = "https://example.com/OpenMW-Setup.exe",
                Sha256 = null,
                ArchiveType = ToolArchiveType.NsisExe,
                ExeProbe = "openmw.exe",
            },
        },
    };

    /// <summary>Records specs and simulates 7z by dropping files into the -o&lt;dir&gt; target.</summary>
    private sealed class Recording7zRunner : Mri.Core.IO.IProcessRunner
    {
        public readonly List<Mri.Core.IO.ProcessSpec> Specs = [];

        public Task<Mri.Core.IO.ProcessResult> RunAsync(
            Mri.Core.IO.ProcessSpec spec,
            IProgress<Mri.Core.IO.OutputLine>? onLine = null,
            CancellationToken ct = default)
        {
            Specs.Add(spec);
            var outDir = spec.Args.FirstOrDefault(a => a.StartsWith("-o"))?[2..];
            if (outDir is not null)
            {
                // What 7z-extracting a real NSIS installer leaves behind:
                // payload plus $PLUGINSDIR runtime junk and an uninstaller.
                Directory.CreateDirectory(Path.Combine(outDir, "$PLUGINSDIR"));
                File.WriteAllText(Path.Combine(outDir, "$PLUGINSDIR", "nsis-junk.dll"), "x");
                Directory.CreateDirectory(Path.Combine(outDir, "resources"));
                File.WriteAllText(Path.Combine(outDir, "openmw.exe"), "MZ");
                File.WriteAllText(Path.Combine(outDir, "Uninstall.exe"), "MZ");
            }
            return Task.FromResult(new Mri.Core.IO.ProcessResult(0, TimeSpan.Zero));
        }
    }

    [Fact]
    public async Task NsisInstallerIsExtractedWith7zNotExecuted()
    {
        var runner = new Recording7zRunner();
        var toolsRoot = Path.Combine(_root, "tools");
        var service = new ToolAcquisitionService(
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("fake nsis installer"u8.ToArray()),
            })),
            runner,
            toolsRoot);

        // Plant the pack's 7z as tool acquisition would have left it.
        var sevenZipName = OperatingSystem.IsWindows() ? "7zmo.exe" : "7zmo";
        var sevenZipPath = Path.Combine(toolsRoot, "momw-tools", sevenZipName);
        Directory.CreateDirectory(Path.GetDirectoryName(sevenZipPath)!);
        File.WriteAllText(sevenZipPath, "MZ");

        await service.EnsureToolAsync(NsisSpec());

        // The one process call must be 7z extraction — never the installer itself
        // (whose /D= switch silently ignores quoted space-containing paths).
        var spec = Assert.Single(runner.Specs);
        Assert.Equal(sevenZipPath, spec.Exe);
        Assert.Contains("x", spec.Args);
        Assert.True(service.IsInstalled(NsisSpec()));

        // NSIS leftovers are cleaned; payload survives.
        var toolDir = service.GetToolDir(NsisSpec());
        Assert.False(Directory.Exists(Path.Combine(toolDir, "$PLUGINSDIR")));
        Assert.False(File.Exists(Path.Combine(toolDir, "Uninstall.exe")));
        Assert.True(Directory.Exists(Path.Combine(toolDir, "resources")));
    }

    [Fact]
    public async Task TarGzToolExtractsWithProbe()
    {
        // Build a tar.gz containing the probe binary, like the Linux packs.
        using var buffer = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(
                   buffer, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
        using (var tar = new System.Formats.Tar.TarWriter(gzip))
        {
            var entry = new System.Formats.Tar.PaxTarEntry(
                System.Formats.Tar.TarEntryType.RegularFile, "pack-1.0/fake-tool")
            {
                DataStream = new MemoryStream("ELF fake"u8.ToArray()),
            };
            tar.WriteEntry(entry);
        }
        var tarBytes = buffer.ToArray();

        var service = new ToolAcquisitionService(
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(tarBytes),
            })),
            new ProcessRunner(),
            Path.Combine(_root, "tools-targz"));

        var spec = new ToolSpec
        {
            Id = "fake-linux-tool",
            Version = "1.0",
            InstallSubdir = "fake-linux-tool",
            Platforms = new Dictionary<string, ToolVariant>
            {
                [ToolManifest.CurrentRid] = new()
                {
                    Url = "https://example.com/pack.tar.gz",
                    ArchiveType = ToolArchiveType.TarGz,
                    ExeProbe = "fake-tool",
                },
            },
        };

        await service.EnsureToolAsync(spec);

        Assert.True(service.IsInstalled(spec));
        Assert.EndsWith(Path.Combine("pack-1.0", "fake-tool"), service.FindExe(spec));
    }

    [Fact]
    public async Task NsisWithoutAny7zOnNonWindowsThrows()
    {
        if (OperatingSystem.IsWindows())
            return; // Windows falls back to the silent installer instead.

        var service = MakeService();
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => service.EnsureToolAsync(NsisSpec()));
    }
}
