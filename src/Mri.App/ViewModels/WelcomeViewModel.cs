namespace Mri.App.ViewModels;

public sealed class WelcomeViewModel(WizardState state) : PageViewModel
{
    public override string Title => "Welcome";

    public int ModCount => state.Data.Modlist.Mods.Count;

    public string DiskEstimate =>
        $"~{Math.Max(1, state.Data.Modlist.EstimatedInstalledBytes / (1024L * 1024 * 1024))} GB";

    public string Blurb =>
        "This installer turns a clean Morrowind (GOTY) installation into a fully modded " +
        $"OpenMW setup with {ModCount} curated mods: bug fixes, high-res assets, quest " +
        "restorations, overhauled lighting, groundcover, and modern quality-of-life additions.\n\n" +
        "It downloads OpenMW and all required tools automatically, fetches every mod from " +
        "Nexus Mods and other sources, and generates a ready-to-play configuration.\n\n" +
        "Requirements:\n" +
        "  •  Morrowind Game of the Year Edition installed (Steam, GOG, or disc)\n" +
        "  •  A Nexus Mods account with Premium (needed for automated downloads)\n" +
        $"  •  Around {DiskEstimate} of free disk space and a few hours of download time";
}
