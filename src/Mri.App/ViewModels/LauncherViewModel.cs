using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mri.App.Services;
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

    public ObservableCollection<QualitySettingRow> ActiveSettings { get; } = [];

    /// <summary>Raised when the user asks to re-run the installer wizard.</summary>
    public event Action? ReinstallRequested;

    public LauncherViewModel(LauncherState state, AppData data)
    {
        _state = state;
        InstallDir = state.InstallDir ?? "";
        ListVersion = data.Modlist.ListVersion;
        ModCount = data.Modlist.Mods.Count;
        _tier = state.Tier ?? QualityTier.Master;
        RefreshSettingRows();
    }

    partial void OnTierChanged(QualityTier value) => RefreshSettingRows();

    private void RefreshSettingRows()
    {
        ActiveSettings.Clear();
        foreach (var (section, key, val) in QualityPresets.Overrides(Tier))
            ActiveSettings.Add(new QualitySettingRow($"{section} · {key}", val));
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
