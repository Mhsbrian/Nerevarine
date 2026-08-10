using Mri.Core.IO;

namespace Mri.Core.Logging;

/// <summary>
/// Decorator that records every child-process invocation verbatim — command
/// line, environment, every stdout/stderr line, exit code, duration. Child CLI
/// output (umo, 7z, delta-plugin, iniimporter…) is the highest-value
/// diagnostic data when debugging a failed run from a log file alone.
/// </summary>
public sealed class LoggingProcessRunner(IProcessRunner inner, InstallLog log) : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        ProcessSpec spec,
        IProgress<OutputLine>? onLine = null,
        CancellationToken ct = default)
    {
        var name = Path.GetFileName(spec.Exe);
        var commandLine = spec.RawArguments ?? string.Join(" ", spec.Args);
        var env = spec.Env.Count > 0
            ? " env{" + string.Join(", ", spec.Env.Select(kv => $"{kv.Key}={MaskSensitive(kv.Key, kv.Value)}")) + "}"
            : "";
        log.Info("proc", $"spawn: \"{spec.Exe}\" {commandLine}{env}" +
                         (spec.WorkingDir is { Length: > 0 } cwd ? $" cwd={cwd}" : ""));

        var tee = new TeeProgress(line =>
        {
            log.Info($"proc:{name}", (line.IsError ? "[stderr] " : "") + line.Text);
            onLine?.Report(line);
        });

        try
        {
            var result = await inner.RunAsync(spec, tee, ct).ConfigureAwait(false);
            log.Info("proc", $"exit: {name} → code {result.ExitCode} after {result.Duration.TotalSeconds:F1}s");
            return result;
        }
        catch (OperationCanceledException)
        {
            log.Warn("proc", $"{name} cancelled");
            throw;
        }
        catch (Exception e)
        {
            log.Error("proc", $"{name} failed to run", e);
            throw;
        }
    }

    /// <summary>
    /// Secret-bearing env vars are masked outright — defense in depth on top
    /// of the log's redaction list.
    /// </summary>
    private static string MaskSensitive(string key, string value) =>
        key.Contains("KEY", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("SECRET", StringComparison.OrdinalIgnoreCase)
            ? Redactor.Mask
            : value;

    /// <summary>Synchronous forwarder — keeps log ordering faithful to output order.</summary>
    private sealed class TeeProgress(Action<OutputLine> handler) : IProgress<OutputLine>
    {
        public void Report(OutputLine value) => handler(value);
    }
}
