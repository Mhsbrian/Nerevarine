using Mri.Core.Nexus;

namespace Mri.Core.Tests.Nexus;

public class ApiKeyStoreTests : IDisposable
{
    private readonly string _dir;

    public ApiKeyStoreTests() =>
        _dir = Directory.CreateTempSubdirectory("mri-keystore-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ApiKeyStore Store() => new(Path.Combine(_dir, "sub", "nexus.key"));

    [Fact]
    public void RoundTripsKey()
    {
        var store = Store();
        store.Save("my-secret-api-key");

        Assert.Equal("my-secret-api-key", store.Load());
    }

    [Fact]
    public void KeyIsNotStoredAsPlaintextOnDisk()
    {
        var store = Store();
        store.Save("my-secret-api-key");

        var raw = File.ReadAllText(Path.Combine(_dir, "sub", "nexus.key"));
        Assert.DoesNotContain("my-secret-api-key", raw);
    }

    [Fact]
    public void MissingFileLoadsNull() => Assert.Null(Store().Load());

    [Fact]
    public void CorruptFileLoadsNull()
    {
        var path = Path.Combine(_dir, "sub", "nexus.key");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not base64 !!!");

        Assert.Null(new ApiKeyStore(path).Load());
    }

    [Fact]
    public void ClearRemovesKey()
    {
        var store = Store();
        store.Save("key");
        store.Clear();

        Assert.Null(store.Load());
    }
}
