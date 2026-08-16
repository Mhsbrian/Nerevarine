using System.Diagnostics;
using System.Text.Json;
using Mri.Core.OpenMw;

namespace Mri.App.Services;

/// <summary>Persisted launcher state (which install to drive, chosen tier).</summary>
public sealed class LauncherState
{
    public string? InstallDir { get; set; }
    public QualityTier? Tier { get; set; }

    private static string PathFor() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Nerevarine", "launcher.json");

    public static LauncherState Load()
    {
        try
        {
            var p = PathFor();
            if (File.Exists(p))
                return JsonSerializer.Deserialize<LauncherState>(File.ReadAllText(p)) ?? new();
        }
        catch { /* corrupt state falls back to defaults */ }
        return new LauncherState();
    }

    public void Save()
    {
        var p = PathFor();
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, JsonSerializer.Serialize(this));
    }

    /// <summary>A directory counts as a playable install when the pipeline
    /// state exists and an OpenMW binary is present.</summary>
    public static bool IsPlayableInstall(string? dir) =>
        dir is not null
        && File.Exists(Path.Combine(dir, "state.json"))
        && GameLauncher.FindOpenMw(dir) is not null;
}

public static class GameLauncher
{
    public static string? FindOpenMw(string installDir)
    {
        var tools = Path.Combine(installDir, "tools", "openmw");
        if (!Directory.Exists(tools))
            return null;
        var name = OperatingSystem.IsWindows() ? "openmw.exe" : "openmw";
        return Directory.EnumerateFiles(tools, name, SearchOption.AllDirectories).FirstOrDefault();
    }

    public static void Play(string installDir)
    {
        var exe = FindOpenMw(installDir)
            ?? throw new InvalidOperationException("OpenMW binary not found under the install directory.");
        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
        });
    }

    /// <summary>The OpenMW user settings.cfg this platform reads.</summary>
    public static string SettingsCfgPath() => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games", "OpenMW", "settings.cfg")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "openmw", "settings.cfg");
}

/// <summary>
/// Makes this app the game's front door: copies the running executable into
/// the install dir as Nerevarine[.exe] and creates a desktop entry named
/// "Nerevarine" pointing at it.
/// </summary>
public static class EntryPointInstaller
{
    public static string Install(string installDir)
    {
        var self = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine the running executable path.");
        var target = Path.Combine(installDir,
            OperatingSystem.IsWindows() ? "Nerevarine.exe" : "Nerevarine");
        if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            File.Copy(self, target, overwrite: true);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(target,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        CreateDesktopEntry(target);
        return target;
    }

    private static void CreateDesktopEntry(string target)
    {
        if (OperatingSystem.IsWindows())
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var lnk = Path.Combine(desktop, "Nerevarine.lnk");
            var script =
                $"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{lnk}');" +
                $"$s.TargetPath='{target}';" +
                $"$s.WorkingDirectory='{Path.GetDirectoryName(target)}';" +
                "$s.Description='Nerevarine — Morrowind, remastered';$s.Save()";
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            })?.WaitForExit(15000);
        }
        else if (OperatingSystem.IsLinux())
        {
            var entry = $"""
                [Desktop Entry]
                Type=Application
                Name=Nerevarine
                Comment=Morrowind, remastered
                Exec="{target}"
                Terminal=false
                Categories=Game;
                """;
            var apps = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "applications");
            Directory.CreateDirectory(apps);
            File.WriteAllText(Path.Combine(apps, "nerevarine.desktop"), entry);
        }
    }
}
