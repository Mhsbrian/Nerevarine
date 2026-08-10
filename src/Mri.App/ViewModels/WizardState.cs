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

    private static string DefaultInstallDir() =>
        OperatingSystem.IsWindows()
            ? @"C:\MorrowindRemake"
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "MorrowindRemake");
}
