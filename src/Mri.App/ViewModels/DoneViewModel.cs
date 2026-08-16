using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mri.App.Services;

namespace Mri.App.ViewModels;

public sealed partial class DoneViewModel(WizardState state) : PageViewModel
{
    [ObservableProperty]
    private string? _diagnosticsMessage;

    [RelayCommand]
    private void SaveDiagnostics()
    {
        try
        {
            var zip = new InstallRunner(state).SaveDiagnostics();
            DiagnosticsMessage = $"Diagnostics saved: {zip}";
        }
        catch (Exception e)
        {
            DiagnosticsMessage = $"Could not create diagnostics bundle: {e.Message}";
        }
    }

    public override string Title => "Done";
    public override bool CanGoNext => false;
    public override bool CanGoBack => false;

    public override void OnActivated() => RegisterEntryPoint();

    public string Message =>
        "Nerevarine is installed. The Nerevarine launcher is now the front door: it sets your " +
        "quality tier and starts the game. The first start takes a little longer while shaders compile.";

    [ObservableProperty]
    private string? _entryPointMessage;

    /// <summary>Installs the launcher as the game's entry point (exe copy +
    /// desktop entry) and records the install for future launcher startups.</summary>
    public void RegisterEntryPoint()
    {
        try
        {
            var target = EntryPointInstaller.Install(state.InstallDir);
            var launcher = LauncherState.Load();
            launcher.InstallDir = state.InstallDir;
            launcher.Save();
            EntryPointMessage = $"Launcher installed: {target} (desktop entry “Nerevarine” created).";
        }
        catch (Exception e)
        {
            EntryPointMessage = $"Could not install the launcher entry point: {e.Message}";
        }
    }

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
