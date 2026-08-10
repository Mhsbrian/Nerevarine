using Mri.Core.IO;

namespace Mri.Core.OpenMw;

/// <summary>
/// Runs delta-plugin's leveled-list/object merge against the phase-1
/// openmw.cfg. delta-plugin resolves openmw.cfg via the OPENMW_CONFIG
/// environment variable (openmw-cfg crate convention); exact contract is
/// re-verified in the M1 smoke run.
/// </summary>
public sealed class DeltaPluginService(IProcessRunner runner)
{
    public async Task MergeAsync(
        string deltaPluginExe,
        string openMwConfigDir,
        string outputOmwaddonPath,
        IProgress<OutputLine>? onLine = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputOmwaddonPath)!);

        var result = await runner.RunAsync(new ProcessSpec
        {
            Exe = deltaPluginExe,
            Args = ["merge", outputOmwaddonPath],
            WorkingDir = openMwConfigDir,
            Env = new Dictionary<string, string> { ["OPENMW_CONFIG"] = openMwConfigDir },
            Timeout = TimeSpan.FromMinutes(30),
        }, onLine, ct).ConfigureAwait(false);

        if (!result.Success)
            throw new InvalidOperationException(
                $"delta-plugin merge failed with exit code {result.ExitCode}.");
        if (!File.Exists(outputOmwaddonPath))
            throw new InvalidOperationException(
                "delta-plugin reported success but produced no merged addon.");
    }
}
