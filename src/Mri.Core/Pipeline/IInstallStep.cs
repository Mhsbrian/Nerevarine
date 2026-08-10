namespace Mri.Core.Pipeline;

public sealed record StepProgress(string Message, double? Fraction = null);

public interface IInstallStep
{
    string Id { get; }
    string Label { get; }

    /// <summary>
    /// Optional steps log their failure and let the install continue
    /// (e.g. the openmw-validator sanity pass).
    /// </summary>
    bool IsOptional => false;

    /// <summary>
    /// Disk-truth check: true when this step's real artifacts are already in
    /// place, letting the engine skip it on resume. Never trusts stored flags
    /// alone.
    /// </summary>
    bool Verify(InstallContext ctx);

    Task RunAsync(InstallContext ctx, IProgress<StepProgress> progress, CancellationToken ct);
}

/// <summary>
/// Raised by the mod-download step when some mods failed and the user hasn't
/// skipped them; the UI turns this into a Retry / Skip-and-continue choice.
/// </summary>
public sealed class ModsFailedException(IReadOnlyList<string> failedMods)
    : Exception($"{failedMods.Count} mod(s) failed to download/install: {string.Join(", ", failedMods.Take(5))}" +
                (failedMods.Count > 5 ? ", …" : ""))
{
    public IReadOnlyList<string> FailedMods { get; } = failedMods;
}
