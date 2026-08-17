using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mri.App.Services;
using Mri.Core.Logging;
using Mri.Core.Pipeline;

namespace Mri.App.ViewModels;

public sealed partial class InstallProgressViewModel(WizardState state, Action onFinished)
    : PageViewModel
{
    public override string Title => "Installing";
    public override bool CanGoBack => !IsRunning;
    public override bool CanGoNext => false;

    public ObservableCollection<StepViewModel> Steps { get; } = [];
    public ObservableCollection<string> LogLines { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(SkipTickedAndContinueCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _headline = "";

    [ObservableProperty]
    private string? _failureMessage;

    [ObservableProperty]
    private string? _logFilePath;

    [ObservableProperty]
    private string? _diagnosticsMessage;

    public ObservableCollection<FailedModRow> FailedMods { get; } = [];

    public bool HasFailedMods => FailedMods.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkipButtonLabel))]
    [NotifyCanExecuteChangedFor(nameof(SkipTickedAndContinueCommand))]
    private int _tickedCount;

    public string SkipButtonLabel => TickedCount > 0
        ? $"Skip the {TickedCount} ticked and continue"
        : "Skip ticked mods and continue";

    private CancellationTokenSource? _cts;
    private InstallContext? _lastContext;

    private void SetFailedMods(IEnumerable<FailedModRow> rows)
    {
        foreach (var old in FailedMods)
            old.PropertyChanged -= OnRowChanged;
        FailedMods.Clear();
        foreach (var row in rows)
        {
            row.PropertyChanged += OnRowChanged;
            FailedMods.Add(row);
        }
        TickedCount = 0;
        OnPropertyChanged(nameof(HasFailedMods));
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FailedModRow.Skip))
            TickedCount = FailedMods.Count(r => r.Skip);
    }

    public override void OnActivated()
    {
        if (!IsRunning)
            _ = RunAsync();
    }

    private async Task RunAsync()
    {
        IsRunning = true;
        FailureMessage = null;
        DiagnosticsMessage = null;
        SetFailedMods([]);
        Headline = "Preparing…";
        _cts = new CancellationTokenSource();

        var runner = new InstallRunner(state);
        InstallLog? log = null;
        try
        {
            log = runner.CreateLog();
            LogFilePath = log.FilePath;

            var engine = runner.BuildEngine(log, out var ctx);
            _lastContext = ctx;

            if (Steps.Count == 0)
                foreach (var step in engine.Steps)
                    Steps.Add(new StepViewModel(step.Id, step.Label));

            var progress = new Progress<EngineProgress>(p => Dispatcher.UIThread.Post(() => Apply(p)));
            var result = await Task.Run(() => engine.RunAsync(ctx, progress, _cts.Token));

            if (result.Success)
            {
                Headline = "Installation complete!";
                onFinished();
            }
            else if (result.Error is DownloaderFailedException downloaderDown)
            {
                // Systemic — one wall, most of the list missing. Skipping
                // mods would be catastrophic and is deliberately not offered.
                Headline = "The downloads hit a wall.";
                FailureMessage = downloaderDown.Message;
            }
            else if (result.Error is ModsFailedException modsFailed)
            {
                Headline = modsFailed.Failures.Count == 1
                    ? "One mod failed to download."
                    : $"{modsFailed.Failures.Count} mods failed to download.";
                SetFailedMods(modsFailed.Failures.Select(f => new FailedModRow(f.Name, f.Reason)));
                FailureMessage =
                    "Everything else is safely on disk — Retry fetches only what is missing. " +
                    "Or tick the mods to leave out and continue without them.";
            }
            else
            {
                Headline = "Installation stopped.";
                FailureMessage = result.Error?.Message ?? "Unknown error — see the log below.";
            }
        }
        catch (OperationCanceledException)
        {
            Headline = "Paused — click Resume to continue where you left off.";
        }
        catch (Exception e)
        {
            log?.Error("app", "installer crashed outside the engine", e);
            Headline = "Installation stopped.";
            FailureMessage = e.Message;
        }
        finally
        {
            log?.Dispose();
            IsRunning = false;
            RaiseNavigationChanged();
        }
    }

    private void Apply(EngineProgress p)
    {
        var step = Steps.FirstOrDefault(s => s.Id == p.StepId);
        if (step is null)
            return;

        step.Status = p.Status;
        if (p.Detail is { } detail)
        {
            step.Detail = detail.Message;
            step.Fraction = detail.Fraction;
            AppendLog($"[{p.StepLabel}] {detail.Message}");
        }

        if (p.Status == StepStatus.Running)
            Headline = p.StepLabel;
    }

    private void AppendLog(string line)
    {
        LogLines.Add(line);
        while (LogLines.Count > 400)
            LogLines.RemoveAt(0);
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private Task Retry() => RunAsync();

    [RelayCommand]
    private void TickAll()
    {
        var target = FailedMods.Any(r => !r.Skip);
        foreach (var row in FailedMods)
            row.Skip = target;
    }

    [RelayCommand]
    private void SaveDiagnostics()
    {
        try
        {
            var zip = new InstallRunner(state).SaveDiagnostics();
            DiagnosticsMessage = $"Diagnostics saved: {zip} — send this file to get help.";
        }
        catch (Exception e)
        {
            DiagnosticsMessage = $"Could not create diagnostics bundle: {e.Message}";
        }
    }

    private bool CanSkipTicked() => TickedCount > 0 && !IsRunning;

    /// <summary>Skips ONLY the ticked mods; the rest are retried by the
    /// re-run (already-arrived mods are never re-downloaded).</summary>
    [RelayCommand(CanExecute = nameof(CanSkipTicked))]
    private Task SkipTickedAndContinue()
    {
        if (_lastContext is { } ctx)
        {
            foreach (var row in FailedMods.Where(r => r.Skip))
            {
                var mod = state.Data.Modlist.Mods.FirstOrDefault(
                    m => m.Name == row.Name || m.Id == row.Name);
                ctx.State.SkippedMods.Add(mod?.Id ?? row.Name);
            }
            ctx.State.SkippedMods = ctx.State.SkippedMods.Distinct().ToList();
            ctx.SaveState();
        }
        return RunAsync();
    }
}

/// <summary>A failed download the user can individually retry (default) or
/// tick to leave out of the install entirely.</summary>
public sealed partial class FailedModRow(string name, string? reason) : ObservableObject
{
    public string Name { get; } = name;
    public string? Reason { get; } = reason;
    public bool HasReason => !string.IsNullOrEmpty(Reason);

    [ObservableProperty]
    private bool _skip;
}

public sealed partial class StepViewModel(string id, string label) : ObservableObject
{
    public string Id { get; } = id;
    public string Label { get; } = label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusIcon), nameof(IsActive))]
    private StepStatus _status = StepStatus.Pending;

    [ObservableProperty]
    private string? _detail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFraction))]
    private double? _fraction;

    public bool HasFraction => Fraction is not null;
    public bool IsActive => Status == StepStatus.Running;

    public string StatusIcon => Status switch
    {
        StepStatus.Pending => "○",
        StepStatus.Running => "◐",
        StepStatus.AlreadyDone => "✓",
        StepStatus.Completed => "✓",
        StepStatus.Failed => "✗",
        StepStatus.SkippedOptional => "⚠",
        _ => "○",
    };
}
