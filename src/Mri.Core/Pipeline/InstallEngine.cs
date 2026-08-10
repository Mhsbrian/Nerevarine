using Mri.Core.Logging;

namespace Mri.Core.Pipeline;

public enum StepStatus
{
    Pending,
    Running,
    AlreadyDone,
    Completed,
    Failed,
    SkippedOptional,
}

public sealed record EngineProgress(
    string StepId,
    string StepLabel,
    StepStatus Status,
    StepProgress? Detail = null);

public sealed record EngineResult(bool Success, string? FailedStepId, Exception? Error);

/// <summary>
/// Runs the ordered step list. A step whose Verify passes is skipped (that is
/// the whole resume story); otherwise it runs and must verify afterwards.
/// State is persisted after every step so a crash resumes cleanly. Every
/// decision is written to the install log so a failure is diagnosable from
/// the log file alone.
/// </summary>
public sealed class InstallEngine(IReadOnlyList<IInstallStep> steps, InstallLog? log = null)
{
    public IReadOnlyList<IInstallStep> Steps => steps;

    public async Task<EngineResult> RunAsync(
        InstallContext ctx,
        IProgress<EngineProgress>? progress = null,
        CancellationToken ct = default)
    {
        log?.Info("engine", $"=== run started: {steps.Count} steps, list {ctx.Modlist.ListVersion}, " +
                            $"install dir '{ctx.InstallDir}', game '{ctx.Game.GameRoot}' ===");

        foreach (var step in steps)
        {
            ct.ThrowIfCancellationRequested();

            if (PreVerify(step, ctx))
            {
                log?.Info(step.Id, "already satisfied on disk — skipping");
                RecordCompletion(ctx, step);
                progress?.Report(new EngineProgress(step.Id, step.Label, StepStatus.AlreadyDone));
                continue;
            }

            log?.Info(step.Id, "starting");
            progress?.Report(new EngineProgress(step.Id, step.Label, StepStatus.Running));
            var stepProgress = new Progress<StepProgress>(p =>
            {
                log?.Info(step.Id, p.Fraction is { } f ? $"{p.Message} ({f:P0})" : p.Message);
                progress?.Report(new EngineProgress(step.Id, step.Label, StepStatus.Running, p));
            });

            try
            {
                await step.RunAsync(ctx, stepProgress, ct).ConfigureAwait(false);

                if (!step.Verify(ctx))
                    throw new InvalidOperationException(
                        $"Step '{step.Label}' reported success but its artifacts failed verification.");

                log?.Info(step.Id, "completed and verified");
                RecordCompletion(ctx, step);
                progress?.Report(new EngineProgress(step.Id, step.Label, StepStatus.Completed));
            }
            catch (OperationCanceledException)
            {
                log?.Warn("engine", $"=== run cancelled during step '{step.Id}' ===");
                ctx.SaveState();
                throw;
            }
            catch (Exception e)
            {
                ctx.SaveState();
                if (step.IsOptional)
                {
                    log?.Warn(step.Id, $"optional step failed, continuing: {e.Message}");
                    progress?.Report(new EngineProgress(step.Id, step.Label, StepStatus.SkippedOptional,
                        new StepProgress(e.Message)));
                    continue;
                }

                log?.Error(step.Id, "step failed", e);
                log?.Error("engine", $"=== run FAILED at step '{step.Id}' ===");
                progress?.Report(new EngineProgress(step.Id, step.Label, StepStatus.Failed,
                    new StepProgress(e.Message)));
                return new EngineResult(false, step.Id, e);
            }
        }

        log?.Info("engine", "=== run finished successfully ===");
        return new EngineResult(true, null, null);
    }

    /// <summary>A verifier that throws must mean "not done", never a crash.</summary>
    private bool PreVerify(IInstallStep step, InstallContext ctx)
    {
        try
        {
            return step.Verify(ctx);
        }
        catch (Exception e)
        {
            log?.Warn(step.Id, $"pre-run verify threw ({e.GetType().Name}: {e.Message}) — treating as not done");
            return false;
        }
    }

    private static void RecordCompletion(InstallContext ctx, IInstallStep step)
    {
        ctx.State.CompletedSteps[step.Id] = DateTimeOffset.UtcNow;
        ctx.State.ListVersion = ctx.Modlist.ListVersion;
        ctx.State.GamePath = ctx.Game.GameRoot;
        ctx.SaveState();
    }
}
