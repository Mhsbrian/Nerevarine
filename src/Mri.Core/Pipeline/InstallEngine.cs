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
/// State is persisted after every step so a crash resumes cleanly.
/// </summary>
public sealed class InstallEngine(IReadOnlyList<IInstallStep> steps)
{
    public IReadOnlyList<IInstallStep> Steps => steps;

    public async Task<EngineResult> RunAsync(
        InstallContext ctx,
        IProgress<EngineProgress>? progress = null,
        CancellationToken ct = default)
    {
        foreach (var step in steps)
        {
            ct.ThrowIfCancellationRequested();

            if (step.Verify(ctx))
            {
                RecordCompletion(ctx, step);
                progress?.Report(new EngineProgress(step.Id, step.Label, StepStatus.AlreadyDone));
                continue;
            }

            progress?.Report(new EngineProgress(step.Id, step.Label, StepStatus.Running));
            var stepProgress = new Progress<StepProgress>(p =>
                progress?.Report(new EngineProgress(step.Id, step.Label, StepStatus.Running, p)));

            try
            {
                await step.RunAsync(ctx, stepProgress, ct).ConfigureAwait(false);

                if (!step.Verify(ctx))
                    throw new InvalidOperationException(
                        $"Step '{step.Label}' reported success but its artifacts failed verification.");

                RecordCompletion(ctx, step);
                progress?.Report(new EngineProgress(step.Id, step.Label, StepStatus.Completed));
            }
            catch (OperationCanceledException)
            {
                ctx.SaveState();
                throw;
            }
            catch (Exception e)
            {
                ctx.SaveState();
                if (step.IsOptional)
                {
                    progress?.Report(new EngineProgress(step.Id, step.Label, StepStatus.SkippedOptional,
                        new StepProgress(e.Message)));
                    continue;
                }
                progress?.Report(new EngineProgress(step.Id, step.Label, StepStatus.Failed,
                    new StepProgress(e.Message)));
                return new EngineResult(false, step.Id, e);
            }
        }

        return new EngineResult(true, null, null);
    }

    private static void RecordCompletion(InstallContext ctx, IInstallStep step)
    {
        ctx.State.CompletedSteps[step.Id] = DateTimeOffset.UtcNow;
        ctx.State.ListVersion = ctx.Modlist.ListVersion;
        ctx.State.GamePath = ctx.Game.GameRoot;
        ctx.SaveState();
    }
}
