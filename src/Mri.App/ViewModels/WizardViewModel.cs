using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mri.App.Services;
using Mri.Core.GameDetection;
using Mri.Core.Nexus;

namespace Mri.App.ViewModels;

public sealed partial class WizardViewModel : ObservableObject
{
    public WizardState State { get; }
    public ObservableCollection<PageViewModel> Pages { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoNextCommand), nameof(GoBackCommand))]
    private PageViewModel _currentPage = null!;

    public WizardViewModel(AppData data, IFilePickerService filePicker)
    {
        State = new WizardState { Data = data };

        var registry = OperatingSystem.IsWindows()
            ? (IRegistryReader)new WindowsRegistryReader()
            : new NullRegistryReader();
        var keyStorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MorrowindRemakeInstaller", "nexus.key");

        var done = new DoneViewModel(State);
        var progress = new InstallProgressViewModel(State, onFinished: () => JumpTo(done));

        Pages.Add(new WelcomeViewModel(State));
        Pages.Add(new GameDetectViewModel(State, new GamePathService(registry), filePicker));
        Pages.Add(new InstallDirViewModel(State, filePicker));
        Pages.Add(new NexusAuthViewModel(State, new NexusApiClient(new HttpClient()), new ApiKeyStore(keyStorePath)));
        Pages.Add(new ReviewViewModel(State));
        Pages.Add(progress);
        Pages.Add(done);

        foreach (var page in Pages)
            page.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(PageViewModel.CanGoNext) or nameof(PageViewModel.CanGoBack))
                {
                    GoNextCommand.NotifyCanExecuteChanged();
                    GoBackCommand.NotifyCanExecuteChanged();
                }
            };

        CurrentPage = Pages[0];
        CurrentPage.IsCurrent = true;
        CurrentPage.OnActivated();
    }

    public int CurrentIndex => Pages.IndexOf(CurrentPage);

    public string NextButtonText => CurrentPage is ReviewViewModel ? "Install" : "Next";

    private bool CanGoNext() => CurrentIndex < Pages.Count - 1 && CurrentPage.CanGoNext;

    private bool CanGoBack() => CurrentIndex > 0 && CurrentPage.CanGoBack
        && CurrentPage is not DoneViewModel;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void GoNext() => JumpTo(Pages[CurrentIndex + 1]);

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => JumpTo(Pages[CurrentIndex - 1]);

    private void JumpTo(PageViewModel page)
    {
        CurrentPage.IsCurrent = false;
        page.IsCurrent = true;
        CurrentPage = page;
        OnPropertyChanged(nameof(CurrentIndex));
        OnPropertyChanged(nameof(NextButtonText));
        page.OnActivated();
        GoNextCommand.NotifyCanExecuteChanged();
        GoBackCommand.NotifyCanExecuteChanged();
    }
}
