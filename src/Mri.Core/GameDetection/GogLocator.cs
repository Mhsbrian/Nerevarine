namespace Mri.Core.GameDetection;

/// <summary>
/// Finds GOG installs by enumerating HKLM\SOFTWARE\[WOW6432Node\]GOG.com\Games\*
/// and reading each game's "path"/"gameName" values.
/// </summary>
public sealed class GogLocator(IRegistryReader registry)
{
    private const string GamesKey = @"SOFTWARE\GOG.com\Games";

    public IReadOnlyList<string> FindMorrowindCandidates()
    {
        var results = new List<string>();

        foreach (var width in new[] { RegistryWidth.Registry32, RegistryWidth.Registry64 })
        {
            foreach (var gameId in registry.GetSubKeyNames(RegistryRoot.LocalMachine, width, GamesKey))
            {
                var subKey = $@"{GamesKey}\{gameId}";
                var name = registry.GetString(RegistryRoot.LocalMachine, width, subKey, "gameName") ?? string.Empty;
                var path = registry.GetString(RegistryRoot.LocalMachine, width, subKey, "path");

                if (path is not null && name.Contains("Morrowind", StringComparison.OrdinalIgnoreCase))
                    results.Add(path);
            }
        }

        return results
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .ToList();
    }
}
