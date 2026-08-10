using System.Diagnostics;

namespace Mri.Core.IO;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        ProcessSpec spec,
        IProgress<OutputLine>? onLine = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = spec.Exe,
            WorkingDirectory = spec.WorkingDir ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in spec.Args)
            psi.ArgumentList.Add(arg);
        foreach (var (key, value) in spec.Env)
            psi.Environment[key] = value;

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi };

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start process: {spec.Exe}");

        using var timeoutCts = spec.Timeout is { } timeout
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;
        timeoutCts?.CancelAfter(spec.Timeout!.Value);
        var effectiveCt = timeoutCts?.Token ?? ct;

        var stdoutTask = PumpAsync(process.StandardOutput, isError: false, onLine, effectiveCt);
        var stderrTask = PumpAsync(process.StandardError, isError: true, onLine, effectiveCt);

        try
        {
            await process.WaitForExitAsync(effectiveCt).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process may already have exited between the cancellation and the kill.
            }

            if (timeoutCts?.IsCancellationRequested == true && !ct.IsCancellationRequested)
                throw new TimeoutException(
                    $"{Path.GetFileName(spec.Exe)} exceeded timeout of {spec.Timeout}.");
            throw;
        }

        stopwatch.Stop();
        return new ProcessResult(process.ExitCode, stopwatch.Elapsed);
    }

    private static async Task PumpAsync(
        StreamReader reader, bool isError, IProgress<OutputLine>? onLine, CancellationToken ct)
    {
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            onLine?.Report(new OutputLine(isError, line));
    }
}
