namespace Mri.Core.GameDetection;

public enum GameSource
{
    Steam,
    Gog,
    BethesdaRegistry,
    DefaultPath,
    Manual,
}

public sealed record GameCandidate(string Path, GameSource Source, GameValidation Validation);

/// <summary>
/// Aggregates every detection strategy into an ordered, validated candidate
/// list for the "Find Morrowind" wizard screen. Steam first — it is by far the
/// most common install — then GOG, the Bethesda registry key, and well-known
/// default paths.
/// </summary>
public sealed class GamePathService(IRegistryReader registry)
{
    private static readonly string[] DefaultPaths =
    [
        @"C:\Program Files (x86)\Steam\steamapps\common\Morrowind",
        @"C:\Program Files\Bethesda Softworks\Morrowind",
        @"C:\GOG Games\Morrowind",
        @"C:\GOG Games\The Elder Scrolls III Morrowind GOTY",
    ];

    public IReadOnlyList<GameCandidate> DetectCandidates()
    {
        var candidates = new List<GameCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path, GameSource source)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            var full = Path.GetFullPath(path);
            if (!seen.Add(full))
                return;
            var validation = GameValidator.Validate(full);
            if (validation.IsValid)
                candidates.Add(new GameCandidate(full, source, validation));
        }

        Add(new SteamLocator(registry).FindMorrowind()?.InstallDir, GameSource.Steam);

        foreach (var gog in new GogLocator(registry).FindMorrowindCandidates())
            Add(gog, GameSource.Gog);

        foreach (var root in new[] { RegistryRoot.LocalMachine, RegistryRoot.CurrentUser })
        foreach (var width in new[] { RegistryWidth.Registry32, RegistryWidth.Registry64 })
            Add(registry.GetString(root, width, @"SOFTWARE\Bethesda Softworks\Morrowind", "Installed Path"),
                GameSource.BethesdaRegistry);

        foreach (var path in DefaultPaths)
            if (Directory.Exists(path))
                Add(path, GameSource.DefaultPath);

        return candidates;
    }

    /// <summary>Validates a user-picked folder as a manual candidate.</summary>
    public static GameCandidate ValidateManual(string path) =>
        new(Path.GetFullPath(path), GameSource.Manual, GameValidator.Validate(path));
}
