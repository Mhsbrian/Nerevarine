using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using Mri.Core.Tools;

namespace Mri.App.ViewModels;

public sealed partial class DoneViewModel(WizardState state) : PageViewModel
{
    public override string Title => "Done";
    public override bool CanGoNext => false;
    public override bool CanGoBack => false;

    public string Message =>
        "Morrowind Remake is installed. Launch OpenMW and start a new game — the first start " +
        "takes a little longer while shaders compile.";

    public bool CanLaunch => OperatingSystem.IsWindows() && FindOpenMw() is not null;

    [RelayCommand]
    private void LaunchOpenMw()
    {
        if (FindOpenMw() is { } exe)
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenInstallFolder() =>
        Process.Start(new ProcessStartInfo(state.InstallDir) { UseShellExecute = true });

    private string? FindOpenMw()
    {
        var toolsDir = Path.Combine(state.InstallDir, "tools");
        if (!Directory.Exists(toolsDir))
            return null;
        var exeName = OperatingSystem.IsWindows() ? "openmw.exe" : "openmw";
        return Directory.EnumerateFiles(toolsDir, exeName, SearchOption.AllDirectories).FirstOrDefault();
    }
}
