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
            var window = new ShellWindow();
            var filePicker = new StorageFilePickerService(() => TopLevel.GetTopLevel(window));
            window.DataContext = new WizardViewModel(AppData.LoadEmbedded(), filePicker);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
