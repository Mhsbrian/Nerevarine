using CommunityToolkit.Mvvm.ComponentModel;
using Mri.App.Services;
using Mri.Core.GameDetection;
using Mri.Core.Nexus;

namespace Mri.App.ViewModels;

/// <summary>Choices accumulated across wizard pages, consumed by the install run.</summary>
public sealed partial class WizardState : ObservableObject
{
    public required AppData Data { get; init; }

    [ObservableProperty]
    private GameCandidate? _game;

    [ObservableProperty]
    private string _installDir = DefaultInstallDir();

    [ObservableProperty]
    private string _nexusApiKey = "";

    [ObservableProperty]
    private NexusUser? _nexusUser;

    public bool IsReadyToInstall =>
        Game is { Validation.IsValid: true } &&
        !string.IsNullOrWhiteSpace(InstallDir) &&
        NexusUser is { IsPremium: true };

    private static string DefaultInstallDir()
    {
        // A prior install (any version) wins: re-entering the wizard is an
        // UPDATE of that install unless the user picks elsewhere.
        var remembered = Services.LauncherState.Load().InstallDir;
        if (!string.IsNullOrWhiteSpace(remembered) && Directory.Exists(remembered))
            return remembered;
        return OperatingSystem.IsWindows()
            ? @"C:\MorrowindRemake"
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "MorrowindRemake");
    }
}
