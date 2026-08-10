using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mri.App.Services;

namespace Mri.App.ViewModels;

public sealed partial class InstallDirViewModel(WizardState state, IFilePickerService filePicker)
    : PageViewModel
{
    public override string Title => "Install location";

    public string InstallDir
    {
        get => state.InstallDir;
        set
        {
            state.InstallDir = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SpaceMessage));
            OnPropertyChanged(nameof(Warning));
            RaiseNavigationChanged();
        }
    }

    public override bool CanGoNext => !string.IsNullOrWhiteSpace(InstallDir);

    public string SpaceMessage
    {
        get
        {
            try
            {
                var probe = new DirectoryInfo(InstallDir);
                while (!probe.Exists && probe.Parent is not null)
                    probe = probe.Parent;
                if (!probe.Exists)
                    return "";
                var free = new DriveInfo(probe.FullName).AvailableFreeSpace;
                var needed = Math.Max(state.Data.Modlist.EstimatedInstalledBytes, 30L * 1024 * 1024 * 1024);
                var freeGb = free / (1024.0 * 1024 * 1024);
                var neededGb = needed / (1024.0 * 1024 * 1024);
                return free < needed
                    ? $"✗ Only {freeGb:F0} GB free — around {neededGb:F0} GB is needed."
                    : $"✓ {freeGb:F0} GB free (≈{neededGb:F0} GB needed).";
            }
            catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
            {
                return "";
            }
        }
    }

    public string? Warning
    {
        get
        {
            if (InstallDir.Contains("OneDrive", StringComparison.OrdinalIgnoreCase))
                return "⚠ This folder is synced by OneDrive — cloud sync fights mod installs. Pick a local folder.";
            if (InstallDir.StartsWith(@"C:\Program Files", StringComparison.OrdinalIgnoreCase))
                return "⚠ Program Files needs admin rights and breaks the downloader — pick e.g. C:\\MorrowindRemake.";
            if (InstallDir.Length > 80)
                return "⚠ Very long paths can hit Windows path-length limits with deeply nested mods.";
            return null;
        }
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var path = await filePicker.PickFolderAsync("Choose where to install the modded setup");
        if (path is not null)
            InstallDir = path;
    }
}
