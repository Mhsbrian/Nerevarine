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

    /// <summary>
    /// Whether the engine re-runs Verify after RunAsync succeeds. Steps whose
    /// only durable signal is the completion record itself (navmesh, validator
    /// — their outputs live outside the install dir) must return false:
    /// completion is recorded AFTER post-verify, so a marker-based Verify can
    /// never pass on first success (field-hit deadlock, twice).
    /// </summary>
    bool VerifyAfterRun => true;

    Task RunAsync(InstallContext ctx, IProgress<StepProgress> progress, CancellationToken ct);
}

/// <summary>One mod that did not arrive, with umo's stated cause when it gave one.</summary>
public sealed record FailedMod(string Name, string? Reason);

/// <summary>
/// Raised by the mod-download step when SPECIFIC mods failed while the rest
/// arrived; the UI turns this into per-mod retry/skip choices.
/// </summary>
public sealed class ModsFailedException(IReadOnlyList<FailedMod> failures)
    : Exception($"{failures.Count} mod(s) failed to download/install: " +
                string.Join(", ", failures.Take(5).Select(f => f.Name)) +
                (failures.Count > 5 ? ", …" : ""))
{
    public IReadOnlyList<FailedMod> Failures { get; } = failures;
}

/// <summary>
/// Raised when the downloader itself hit a wall — zero mods arrived and one
/// root cause (bad Nexus key, rate limit, dead network) explains every error.
/// This is a fix-and-retry situation, never a per-mod one: the UI must show
/// the cause and must NOT offer to skip the entire modlist over it.
/// </summary>
public sealed class DownloaderFailedException(string userMessage, string? dominantError, int pendingCount)
    : Exception(userMessage)
{
    /// <summary>The normalized error line shared by (nearly) every failure, if one dominated.</summary>
    public string? DominantError { get; } = dominantError;

    /// <summary>How many mods were waiting to download when the run stalled.</summary>
    public int PendingCount { get; } = pendingCount;
}
