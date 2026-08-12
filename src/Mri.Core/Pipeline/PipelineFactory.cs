using Mri.Core.IO;
using Mri.Core.OpenMw;
using Mri.Core.Pipeline.Steps;
using Mri.Core.Tools;
using Mri.Core.Umo;

namespace Mri.Core.Pipeline;

/// <summary>
/// Wires the production pipeline: real services, canonical step order.
/// </summary>
public static class PipelineFactory
{
    public static InstallEngine Create(
        InstallContext ctx,
        HttpClient http,
        IProcessRunner runner,
        string settingsTemplate,
        string shadersTemplate,
        Logging.InstallLog? log = null,
        string? fixupsSourceDir = null)
    {
        var tools = new ToolAcquisitionService(http, runner, ctx.ToolsDir);
        var locator = new ToolLocator(tools, ctx.ToolManifest);
        var umoConfig = new UmoConfigWriter(ctx.UmoConfDir);
        var umo = new UmoService(runner, () => locator.UmoExe, ctx.UmoConfDir, ctx.NexusApiKey);

        var steps = new List<IInstallStep>
        {
            new AcquireToolsStep(tools),
            new WriteUmoConfigStep(umoConfig, c => new UmoSettings
            {
                ModBaseDir = c.ModsRootDir,
                CacheDir = c.DownloadCacheDir,
                Tes3cmdPath = locator.Tes3cmdExe ?? "",
            }),
            new RegisterModlistStep(umo),
            new InstallModsStep(umo),
            new InstallFixupsStep(fixupsSourceDir),
            new ImportIniStep(new IniImporterService(runner), _ => locator.IniImporterExe),
            new GenerateOpenMwCfgStep(),
            new GenerateSettingsStep(settingsTemplate, shadersTemplate),
            new DeltaMergeStep(new DeltaPluginService(runner), _ => locator.DeltaPluginExe),
            new NavmeshStep(new NavmeshService(runner), _ => locator.NavmeshToolExe),
            new ValidateStep(runner, _ => locator.ValidatorExe),
        };

        return new InstallEngine(steps, log);
    }
}
