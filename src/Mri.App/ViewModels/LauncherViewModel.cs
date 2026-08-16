using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mri.App.Services;
using Mri.Core.GameDetection;
using Mri.Core.OpenMw;

namespace Mri.App.ViewModels;

public sealed partial class LauncherViewModel : ObservableObject
{
    private readonly LauncherState _state;

    public string InstallDir { get; }
    public string ListVersion { get; }
    public int ModCount { get; }

    [ObservableProperty] private QualityTier _tier;
    [ObservableProperty] private string _statusLine = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateBanner = "";

    private readonly AppData _data;
    private string? _installedListVersion;
    private string? _gamePath;

    public ObservableCollection<QualitySettingRow> ActiveSettings { get; } = [];
    public ObservableCollection<EngineFlagRow> EngineFlags { get; } = [];

    /// <summary>Raised when the user asks to re-run the installer wizard.</summary>
    public event Action? ReinstallRequested;

    public LauncherViewModel(LauncherState state, AppData data)
    {
        _state = state;
        InstallDir = state.InstallDir ?? "";
        ListVersion = data.Modlist.ListVersion;
        ModCount = data.Modlist.Mods.Count;
        _tier = state.Tier ?? QualityTier.Master;
        _data = data;
        RefreshSettingRows();
        LoadEngineFlags();
        DetectUpdate();
    }

    private void DetectUpdate()
    {
        try
        {
            var statePath = Path.Combine(InstallDir, "state.json");
            if (!File.Exists(statePath)) return;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(statePath));
            _installedListVersion = doc.RootElement.TryGetProperty("listVersion", out var lv)
                ? lv.GetString()
                : doc.RootElement.TryGetProperty("ListVersion", out var lv2) ? lv2.GetString() : null;
            _gamePath = doc.RootElement.TryGetProperty("gamePath", out var gp)
                ? gp.GetString()
                : doc.RootElement.TryGetProperty("GamePath", out var gp2) ? gp2.GetString() : null;
            if (_installedListVersion is not null
                && !string.Equals(_installedListVersion, ListVersion, StringComparison.Ordinal))
            {
                UpdateAvailable = true;
                UpdateBanner = $"A newer canon is inscribed in this launcher — {_installedListVersion} → {ListVersion}.";
            }
        }
        catch { /* update detection is best-effort */ }
    }

    [RelayCommand]
    private async Task ApplyUpdateAsync()
    {
        if (Busy) return;
        var gamePath = _gamePath;
        if (gamePath is null || !GameValidator.Validate(gamePath).IsValid)
        {
            StatusLine = "The game folder moved — use Verify / reinstall to point at it again.";
            return;
        }
        Busy = true;
        StatusLine = "Amending the canon…";
        try
        {
            var wizard = new WizardState
            {
                Data = _data,
                InstallDir = InstallDir,
                Game = new GameCandidate(gamePath, GameSource.Manual, GameValidator.Validate(gamePath)),
                NexusApiKey = "",
            };
            var runner = new InstallRunner(wizard);
            using var log = runner.CreateLog();
            var engine = runner.BuildEngine(log, out var ctx);
            var progress = new Progress<Mri.Core.Pipeline.EngineProgress>(p =>
                StatusLine = $"{p.StepLabel}…");
            var result = await Task.Run(() => engine.RunAsync(ctx, progress));
            if (result.Success)
            {
                UpdateAvailable = false;
                StatusLine = "The canon is current. Vvardenfell awaits.";
            }
            else
            {
                StatusLine = result.FailedStepId == "install-mods"
                    ? "New mods need your Nexus sign-in — use Verify / reinstall below."
                    : $"Update stopped at '{result.FailedStepId}': {result.Error?.Message ?? "see the log in the install folder"}";
            }
        }
        catch (Exception e)
        {
            StatusLine = e.Message.Contains("failed to download", StringComparison.OrdinalIgnoreCase)
                ? "New mods need your Nexus sign-in — use Verify / reinstall below."
                : $"Update failed: {e.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private void LoadEngineFlags()
    {
        string cfg;
        try { cfg = File.ReadAllText(GameLauncher.SettingsCfgPath()); }
        catch { cfg = ""; }
        foreach (var flag in ModEngineFlags.All)
            EngineFlags.Add(new EngineFlagRow(flag, ModEngineFlags.Read(cfg, flag)));
    }

    partial void OnTierChanged(QualityTier value) => RefreshSettingRows();

    private void RefreshSettingRows()
    {
        ActiveSettings.Clear();
        foreach (var (aspect, meaning) in QualityPresets.Describe(Tier))
            ActiveSettings.Add(new QualitySettingRow(aspect, meaning));
    }

    [RelayCommand]
    private void SelectTier(QualityTier tier) => Tier = tier;

    [RelayCommand]
    private void AutoDetect()
    {
        Tier = QualityPresets.Detect();
        StatusLine = $"The stars favor {TierName(Tier)}.";
    }

    [RelayCommand]
    private void Play()
    {
        try
        {
            Busy = true;
            QualityPresets.ApplyToFile(GameLauncher.SettingsCfgPath(), Tier);
            var path = GameLauncher.SettingsCfgPath();
            var cfg = File.Exists(path) ? File.ReadAllText(path) : "";
            foreach (var row in EngineFlags)
                cfg = ModEngineFlags.Write(cfg, row.Flag, row.IsOn);
            Mri.Core.IO.AtomicFile.WriteAllText(path, cfg);
            _state.Tier = Tier;
            _state.Save();
            GameLauncher.Play(InstallDir);
            StatusLine = "Vvardenfell awaits.";
        }
        catch (Exception e)
        {
            StatusLine = $"Failed to launch: {e.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = System.IO.Path.Combine(InstallDir, "mods"),
                UseShellExecute = true,
            });
        }
        catch (Exception e)
        {
            StatusLine = $"Could not open folder: {e.Message}";
        }
    }

    [RelayCommand]
    private void Reinstall() => ReinstallRequested?.Invoke();

    public static string TierName(QualityTier tier) => tier switch
    {
        QualityTier.Apprentice => "Apprentice",
        QualityTier.Adept => "Adept",
        _ => "Master",
    };
}

public sealed record QualitySettingRow(string Name, string Value);

public sealed partial class EngineFlagRow(EngineFlag flag, bool isOn) : ObservableObject
{
    public EngineFlag Flag { get; } = flag;
    public string Label => Flag.Label;
    public string RequiredBy => Flag.RequiredBy;

    [ObservableProperty]
    private bool _isOn = isOn;
}
