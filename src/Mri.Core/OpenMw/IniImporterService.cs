using Mri.Core.IO;

namespace Mri.Core.OpenMw;

/// <summary>
/// Drives openmw-iniimporter (ships with OpenMW) to convert the game's
/// Morrowind.ini into openmw.cfg fallback= lines — exactly what the OpenMW
/// wizard would do, minus the wizard.
/// </summary>
public sealed class IniImporterService(IProcessRunner runner)
{
    /// <summary>Returns the fallback= values (sans prefix) harvested from the import.</summary>
    public async Task<IReadOnlyList<string>> ImportFallbackLinesAsync(
        string iniImporterExe,
        string morrowindIniPath,
        string encoding = "win1252",
        IProgress<OutputLine>? onLine = null,
        CancellationToken ct = default)
    {
        var workDir = Directory.CreateTempSubdirectory("mri-iniimport-").FullName;
        try
        {
            var inputCfg = Path.Combine(workDir, "in.cfg");
            var outputCfg = Path.Combine(workDir, "out.cfg");
            File.WriteAllText(inputCfg, string.Empty);

            var result = await runner.RunAsync(new ProcessSpec
            {
                Exe = iniImporterExe,
                Args =
                [
                    "--ini", morrowindIniPath,
                    "--cfg", inputCfg,
                    "--output", outputCfg,
                    "--encoding", encoding,
                ],
                Timeout = TimeSpan.FromMinutes(2),
            }, onLine, ct).ConfigureAwait(false);

            if (!result.Success || !File.Exists(outputCfg))
                throw new InvalidOperationException(
                    $"openmw-iniimporter failed with exit code {result.ExitCode}.");

            return OpenMwCfg.Parse(File.ReadAllText(outputCfg)).GetValues("fallback");
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort temp cleanup.
            }
        }
    }
}
