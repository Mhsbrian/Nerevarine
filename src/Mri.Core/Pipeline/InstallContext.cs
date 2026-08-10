using Mri.Core.GameDetection;
using Mri.Core.OpenMw;
using Mri.Core.Tools;

namespace Mri.Core.Pipeline;

/// <summary>
/// Everything a step needs to know about THIS install run: user choices,
/// derived directory layout, and the mutable audit state. Services live in
/// the steps themselves (constructor-injected) so they're fakeable.
/// </summary>
public sealed class InstallContext
{
    public required string InstallDir { get; init; }
    public required GameValidation Game { get; init; }
    public required Modlist.Modlist Modlist { get; init; }
    public required ToolManifest ToolManifest { get; init; }
    public required string NexusApiKey { get; init; }
    public required OpenMwUserPaths OpenMwPaths { get; init; }
    public string AppVersion { get; init; } = "0.1.0";
    public int DownloadThreads { get; init; } = 4;
    public bool NexusPremium { get; init; } = true;

    public required InstallState State { get; init; }
    public required InstallStateStore StateStore { get; init; }

    // Derived layout — a single install dir owns everything.
    public string ModsRootDir => Path.Combine(InstallDir, "mods");
    public string ToolsDir => Path.Combine(InstallDir, "tools");
    public string UmoConfDir => Path.Combine(InstallDir, "umo-conf");
    public string DownloadCacheDir => Path.Combine(InstallDir, "downloads");
    public string ModlistDir => Path.Combine(InstallDir, "modlist");
    public string LogsDir => Path.Combine(InstallDir, "logs");
    public string EmittedUmoListPath => Path.Combine(ModlistDir, $"{Modlist.Name}.json");
    public string UmoListName => Modlist.Name;

    public void SaveState() => StateStore.Save(State);

    public Modlist.LoadOrderOptions LoadOrderOptions(bool includeDelta) => new()
    {
        IncludeDelta = includeDelta,
        SkippedModIds = State.SkippedMods.ToHashSet(),
    };

    public CfgComposition CfgComposition(bool includeDelta) => new()
    {
        GameDataFilesDir = Game.DataFilesDir
            ?? throw new InvalidOperationException("Game validation has no Data Files dir."),
        ModsRootDir = ModsRootDir,
        Plan = Core.Modlist.ModlistCompiler.BuildLoadOrderPlan(Modlist, LoadOrderOptions(includeDelta)),
        FallbackLines = State.FallbackLines,
        HasTribunal = Game.HasTribunal,
        HasBloodmoon = Game.HasBloodmoon,
        AppVersion = AppVersion,
        ListVersion = Modlist.ListVersion,
    };
}
