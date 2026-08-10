using Mri.Core.IO;

namespace Mri.Core.Tests.IO;

public class AtomicFileTests : IDisposable
{
    private readonly string _dir;

    public AtomicFileTests() =>
        _dir = Directory.CreateTempSubdirectory("mri-atomic-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void CreatesNewFileAndParentDirectories()
    {
        var path = Path.Combine(_dir, "sub", "dir", "file.txt");
        AtomicFile.WriteAllText(path, "hello");

        Assert.Equal("hello", File.ReadAllText(path));
    }

    [Fact]
    public void OverwritesExistingFile()
    {
        var path = Path.Combine(_dir, "file.txt");
        File.WriteAllText(path, "old");

        AtomicFile.WriteAllText(path, "new");
        Assert.Equal("new", File.ReadAllText(path));
    }

    [Fact]
    public void LeavesNoTempFilesBehind()
    {
        var path = Path.Combine(_dir, "file.txt");
        AtomicFile.WriteAllText(path, "one");
        AtomicFile.WriteAllText(path, "two");

        Assert.Single(Directory.GetFiles(_dir));
    }
}
