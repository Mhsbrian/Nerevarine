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
        Url = "https://example.com/fake-tool.zip",
        Sha256 = sha256,
        ArchiveType = ToolArchiveType.Zip,
        InstallSubdir = "fake-tool",
        ExeProbe = "fake-tool.exe",
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
}
