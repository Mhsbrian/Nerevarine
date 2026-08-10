using Mri.Core.GameDetection;

namespace Mri.Core.Tests.GameDetection;

public class GameValidatorTests : IDisposable
{
    private readonly string _root;

    public GameValidatorTests() =>
        _root = Directory.CreateTempSubdirectory("mri-validator-test-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string MakeGameDir(
        string dataFilesName = "Data Files",
        string esmName = "Morrowind.esm",
        string bsaName = "Morrowind.bsa",
        bool withIni = true,
        bool withExpansions = false)
    {
        var game = Path.Combine(_root, "Morrowind");
        var dataFiles = Path.Combine(game, dataFilesName);
        Directory.CreateDirectory(dataFiles);
        File.WriteAllText(Path.Combine(dataFiles, esmName), "esm");
        File.WriteAllText(Path.Combine(dataFiles, bsaName), "bsa");
        if (withIni)
            File.WriteAllText(Path.Combine(game, "Morrowind.ini"), "[General]");
        if (withExpansions)
        {
            File.WriteAllText(Path.Combine(dataFiles, "Tribunal.esm"), "esm");
            File.WriteAllText(Path.Combine(dataFiles, "Bloodmoon.esm"), "esm");
        }
        return game;
    }

    [Fact]
    public void ValidGotyInstallPassesWithExpansions()
    {
        var game = MakeGameDir(withExpansions: true);
        var result = GameValidator.Validate(game);

        Assert.True(result.IsValid);
        Assert.Equal(game, result.GameRoot);
        Assert.Equal(Path.Combine(game, "Data Files"), result.DataFilesDir);
        Assert.Equal(Path.Combine(game, "Morrowind.ini"), result.MorrowindIniPath);
        Assert.True(result.HasTribunal);
        Assert.True(result.HasBloodmoon);
    }

    [Fact]
    public void AcceptsDataFilesDirectoryDirectly()
    {
        var game = MakeGameDir();
        var result = GameValidator.Validate(Path.Combine(game, "Data Files"));

        Assert.True(result.IsValid);
        Assert.Equal(game, result.GameRoot);
        Assert.Equal(Path.Combine(game, "Morrowind.ini"), result.MorrowindIniPath);
    }

    [Fact]
    public void CaseInsensitiveOnCaseSensitiveFilesystem()
    {
        var game = MakeGameDir(esmName: "MORROWIND.ESM", bsaName: "morrowind.BSA");
        var result = GameValidator.Validate(game);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void MissingBsaFails()
    {
        var game = MakeGameDir();
        File.Delete(Path.Combine(game, "Data Files", "Morrowind.bsa"));

        var result = GameValidator.Validate(game);
        Assert.False(result.IsValid);
        Assert.Contains("Morrowind.bsa", result.FailReason);
    }

    [Fact]
    public void EmptyDirectoryFails()
    {
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);

        var result = GameValidator.Validate(empty);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void NonexistentDirectoryFails()
    {
        var result = GameValidator.Validate(Path.Combine(_root, "nope"));
        Assert.False(result.IsValid);
        Assert.Contains("does not exist", result.FailReason);
    }

    [Fact]
    public void MissingIniStillValidButIniPathNull()
    {
        var game = MakeGameDir(withIni: false);
        var result = GameValidator.Validate(game);

        Assert.True(result.IsValid);
        Assert.Null(result.MorrowindIniPath);
    }
}
