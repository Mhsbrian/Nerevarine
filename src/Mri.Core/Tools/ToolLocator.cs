namespace Mri.Core.Tools;

/// <summary>
/// Resolves the concrete exe paths of every bundled tool after acquisition.
/// The MOMW tools pack's internal layout is not a contract, so everything is
/// probed by filename rather than by fixed subpath.
/// </summary>
public sealed class ToolLocator(ToolAcquisitionService acquisition, ToolManifest manifest)
{
    public const string OpenMwId = "openmw";
    public const string MomwPackId = "momw-tools-pack";

    public string? OpenMwExe => Find(OpenMwId, "openmw.exe");
    public string? OpenMwLauncherExe => Find(OpenMwId, "openmw-launcher.exe");
    public string? IniImporterExe => Find(OpenMwId, "openmw-iniimporter.exe");
    public string? NavmeshToolExe => Find(OpenMwId, "openmw-navmeshtool.exe");
    public string? ValidatorExe => Find(MomwPackId, "openmw-validator.exe");

    public string? UmoExe => Find(MomwPackId, "umo.exe");
    public string? DeltaPluginExe => Find(MomwPackId, "delta_plugin.exe");
    public string? GroundcoverifyExe => Find(MomwPackId, "groundcoverify.exe");
    public string? Tes3cmdExe => Find(MomwPackId, "tes3cmd.exe");
    public string? SevenZipExe => acquisition.Find7zAnywhere();

    public string OpenMwDir => acquisition.GetToolDir(manifest.Get(OpenMwId));

    private string? Find(string toolId, string exeName) =>
        acquisition.FindExe(manifest.Get(toolId), exeName);
}
