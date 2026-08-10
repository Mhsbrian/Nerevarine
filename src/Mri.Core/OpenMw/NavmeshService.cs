using Mri.Core.IO;

namespace Mri.Core.OpenMw;

/// <summary>
/// Pre-generates the navmesh cache so first launch doesn't stutter through
/// hours of background builds. Reads the final openmw.cfg; long-running
/// (30+ minutes on the full list) and safe to re-run.
/// </summary>
public sealed class NavmeshService(IProcessRunner runner)
{
    public async Task GenerateAsync(
        string navmeshToolExe,
        string openMwConfigDir,
        IProgress<OutputLine>? onLine = null,
        CancellationToken ct = default)
    {
        var result = await runner.RunAsync(new ProcessSpec
        {
            Exe = navmeshToolExe,
            WorkingDir = openMwConfigDir,
            Env = new Dictionary<string, string> { ["OPENMW_CONFIG"] = openMwConfigDir },
            Timeout = TimeSpan.FromHours(4),
        }, onLine, ct).ConfigureAwait(false);

        if (!result.Success)
            throw new InvalidOperationException(
                $"openmw-navmeshtool failed with exit code {result.ExitCode}.");
    }
}
