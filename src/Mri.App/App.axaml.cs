using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Mri.App.Services;
using Mri.App.ViewModels;
using Mri.App.Views;

namespace Mri.App;

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var state = LauncherState.Load();
            desktop.MainWindow = LauncherState.IsPlayableInstall(state.InstallDir)
                ? BuildLauncher(desktop, state)
                : BuildWizard(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Window BuildLauncher(IClassicDesktopStyleApplicationLifetime desktop, LauncherState state)
    {
        var window = new LauncherWindow();
        var vm = new LauncherViewModel(state, AppData.LoadEmbedded());
        vm.IsWindowActive = () => window.IsActive;
        vm.ReinstallRequested += () =>
        {
            var wizard = BuildWizard(desktop);
            desktop.MainWindow = wizard;
            wizard.Show();
            window.Close();
        };
        window.DataContext = vm;
        return window;
    }

    private static Window BuildWizard(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var window = new ShellWindow();
        var filePicker = new StorageFilePickerService(() => TopLevel.GetTopLevel(window));
        window.DataContext = new WizardViewModel(AppData.LoadEmbedded(), filePicker);
        return window;
    }
}
