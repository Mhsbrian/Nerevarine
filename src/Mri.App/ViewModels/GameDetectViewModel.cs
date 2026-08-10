using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mri.App.Services;
using Mri.Core.GameDetection;

namespace Mri.App.ViewModels;

public sealed partial class GameDetectViewModel(
    WizardState state,
    GamePathService gamePaths,
    IFilePickerService filePicker) : PageViewModel
{
    public override string Title => "Find Morrowind";

    public ObservableCollection<GameCandidateViewModel> Candidates { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext), nameof(ValidationMessage), nameof(HasValidSelection))]
    private GameCandidateViewModel? _selected;

    [ObservableProperty]
    private string? _browseError;

    public override bool CanGoNext => Selected is { Candidate.Validation.IsValid: true };

    public bool HasValidSelection => CanGoNext;

    public string ValidationMessage => Selected switch
    {
        null => "Select your Morrowind installation, or browse to it manually.",
        { Candidate.Validation: { IsValid: true, HasTribunal: true, HasBloodmoon: true } } =>
            "✓ Complete GOTY installation — Morrowind, Tribunal and Bloodmoon found.",
        { Candidate.Validation: { IsValid: true } } =>
            "⚠ Morrowind found, but Tribunal/Bloodmoon are missing — the GOTY edition is required by many mods.",
        { Candidate.Validation.FailReason: { } reason } => $"✗ {reason}",
        _ => "",
    };

    public override void OnActivated()
    {
        if (Candidates.Count == 0)
            Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        Candidates.Clear();
        foreach (var candidate in gamePaths.DetectCandidates())
            Candidates.Add(new GameCandidateViewModel(candidate));
        Selected = Candidates.FirstOrDefault();
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        BrowseError = null;
        var path = await filePicker.PickFolderAsync("Select your Morrowind installation folder");
        if (path is null)
            return;

        var manual = GamePathService.ValidateManual(path);
        var vm = new GameCandidateViewModel(manual);
        Candidates.Add(vm);
        Selected = vm;
        if (!manual.Validation.IsValid)
            BrowseError = manual.Validation.FailReason;
    }

    partial void OnSelectedChanged(GameCandidateViewModel? value)
    {
        state.Game = value?.Candidate;
        RaiseNavigationChanged();
    }
}

public sealed class GameCandidateViewModel(GameCandidate candidate)
{
    public GameCandidate Candidate { get; } = candidate;

    public string Path => Candidate.Path;

    public string SourceBadge => Candidate.Source switch
    {
        GameSource.Steam => "Steam",
        GameSource.Gog => "GOG",
        GameSource.BethesdaRegistry => "Registry",
        GameSource.DefaultPath => "Default location",
        _ => "Manual",
    };

    public string StatusIcon => Candidate.Validation.IsValid ? "✓" : "✗";
}
