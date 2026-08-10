namespace Mri.Core.OpenMw;

/// <summary>
/// Where OpenMW reads its user configuration. Windows: Documents\My Games\OpenMW.
/// Linux (dev machines): ~/.config/openmw.
/// </summary>
public sealed class OpenMwUserPaths(string configDir)
{
    public string ConfigDir { get; } = configDir;
    public string OpenMwCfgPath => Path.Combine(ConfigDir, "openmw.cfg");
    public string SettingsCfgPath => Path.Combine(ConfigDir, "settings.cfg");
    public string ShadersYamlPath => Path.Combine(ConfigDir, "shaders.yaml");

    public static OpenMwUserPaths Detect()
    {
        string dir;
        if (OperatingSystem.IsWindows())
        {
            dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "My Games", "OpenMW");
        }
        else
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            dir = Path.Combine(
                string.IsNullOrEmpty(xdg)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
                    : xdg,
                "openmw");
        }
        return new OpenMwUserPaths(dir);
    }
}
