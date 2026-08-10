namespace Mri.App.ViewModels;

public sealed class ReviewViewModel(WizardState state) : PageViewModel
{
    public override string Title => "Review";

    public override void OnActivated()
    {
        OnPropertyChanged(nameof(GamePath));
        OnPropertyChanged(nameof(InstallDir));
        OnPropertyChanged(nameof(Account));
        OnPropertyChanged(nameof(ModCount));
        OnPropertyChanged(nameof(Estimate));
    }

    public string GamePath => state.Game?.Path ?? "—";
    public string InstallDir => state.InstallDir;
    public string Account => state.NexusUser?.Name ?? "—";
    public int ModCount => state.Data.Modlist.Mods.Count;

    public string Estimate =>
        $"≈{Math.Max(1, state.Data.Modlist.EstimatedInstalledBytes / (1024L * 1024 * 1024))} GB " +
        "on disk; several hours including downloads and navmesh generation.";

    public string NextStepNote =>
        "Clicking Install starts the fully automated setup. You can close the app at any point — " +
        "re-running it resumes exactly where it stopped.";
}
