namespace Mri.Core.IO;

public sealed record ProcessSpec
{
    public required string Exe { get; init; }
    public IReadOnlyList<string> Args { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Verbatim command-line string used INSTEAD of <see cref="Args"/> when
    /// set. Needed for switches that must never be quoted — NSIS's /D= ignores
    /// a quoted path, and ArgumentList auto-quotes anything with a space.
    /// </summary>
    public string? RawArguments { get; init; }
    public string? WorkingDir { get; init; }
    public IReadOnlyDictionary<string, string> Env { get; init; } =
        new Dictionary<string, string>();
    public TimeSpan? Timeout { get; init; }
}

public readonly record struct OutputLine(bool IsError, string Text);

public sealed record ProcessResult(int ExitCode, TimeSpan Duration)
{
    public bool Success => ExitCode == 0;
}

/// <summary>
/// The seam through which every child CLI (umo, delta-plugin, iniimporter,
/// navmeshtool, 7z...) is run. Streams output line-by-line so progress parsers
/// can watch it live; faked in tests with captured stdout fixtures.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        ProcessSpec spec,
        IProgress<OutputLine>? onLine = null,
        CancellationToken ct = default);
}

/// <summary>
/// IProgress that invokes its handler on the reporting thread. Progress&lt;T&gt;
/// posts asynchronously, so a child process's final lines — exactly where umo
/// prints its failure summary — can land AFTER the caller has already
/// snapshotted results. Parsers that feed decisions (not just UI) need this.
/// </summary>
public sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
