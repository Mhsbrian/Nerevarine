namespace Mri.Core.Tools;

/// <summary>
/// Resolves the concrete binary paths of every bundled tool after acquisition.
/// Pack layouts differ per platform (Windows: umo.exe launcher + nested dirs;
/// Linux: flat binaries, arch-suffixed validator), so everything is probed by
/// filename rather than by fixed subpath.
/// </summary>
public sealed class ToolLocator(ToolAcquisitionService acquisition, ToolManifest manifest)
{
    public const string OpenMwId = "openmw";
    public const string MomwPackId = "momw-tools-pack";

    private static bool Win => OperatingSystem.IsWindows();

    private static string Exe(string name) => Win ? name + ".exe" : name;

    public string? OpenMwExe => Find(OpenMwId, Exe("openmw"));
    public string? OpenMwLauncherExe => Find(OpenMwId, Exe("openmw-launcher"));
    public string? IniImporterExe => Find(OpenMwId, Exe("openmw-iniimporter"));
    public string? NavmeshToolExe => Find(OpenMwId, Exe("openmw-navmeshtool"));

    public string? ValidatorExe =>
        Find(MomwPackId, Win ? "openmw-validator.exe" : "openmw-validator-linux-amd64");

    public string? UmoExe => Find(MomwPackId, Exe("umo"));
    public string? DeltaPluginExe => Find(MomwPackId, Exe("delta_plugin"));
    public string? GroundcoverifyExe => Find(MomwPackId, Exe("groundcoverify"));
    public string? Tes3cmdExe => Find(MomwPackId, Exe("tes3cmd"));
    public string? SevenZipExe => acquisition.Find7zAnywhere();

    public string OpenMwDir => acquisition.GetToolDir(manifest.Get(OpenMwId));

    private string? Find(string toolId, string exeName) =>
        acquisition.FindExe(manifest.Get(toolId), exeName);
}
