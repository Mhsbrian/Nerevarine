using Mri.Core.IO;

namespace Mri.Core.Umo;

public sealed record UmoInstallResult(
    ProcessResult Process,
    IReadOnlyList<string> FailedMods,
    IReadOnlyDictionary<string, string> FailureReasons,
    IReadOnlyList<string> ErrorLines,
    int? LastCurrent,
    int? LastTotal);

/// <summary>
/// Drives the umo CLI headlessly. Every invocation carries
/// UMO_CONF_DIR=&lt;our conf dir&gt; so umo reads the config we pre-wrote and
/// never the user's own.
/// </summary>
public sealed class UmoService(
    IProcessRunner runner,
    Func<string?> umoExeResolver,
    string confDir,
    string? nexusApiKey = null)
{
    public UmoService(IProcessRunner runner, string umoExe, string confDir, string? nexusApiKey = null)
        : this(runner, () => umoExe, confDir, nexusApiKey)
    {
    }

    /// <summary>Resolved per call — the binary only exists after tool acquisition.</summary>
    private string UmoExe => umoExeResolver()
        ?? throw new InvalidOperationException(
            "umo.exe not found — tool acquisition has not run or an antivirus removed it.");

    private Dictionary<string, string> BaseEnv
    {
        get
        {
            var env = new Dictionary<string, string>
            {
                ["UMO_CONF_DIR"] = confDir,
            };
            // The key rides the environment (umo's documented override), never
            // the on-disk config.json.
            if (!string.IsNullOrEmpty(nexusApiKey))
                env["UMO_NEXUS_API_KEY"] = nexusApiKey;
            return env;
        }
    }

    public async Task AddListAsync(
        string listJsonPath,
        string listName,
        IProgress<OutputLine>? onLine = null,
        CancellationToken ct = default)
    {
        var result = await runner.RunAsync(new ProcessSpec
        {
            Exe = UmoExe,
            Args = ["list", "add", listJsonPath, "--list-name", listName],
            Env = BaseEnv,
            Timeout = TimeSpan.FromMinutes(5),
        }, onLine, ct).ConfigureAwait(false);

        if (!result.Success)
            throw new InvalidOperationException(
                $"umo list add failed with exit code {result.ExitCode}.");
    }

    public async Task<UmoInstallResult> InstallAsync(
        string listName,
        bool nexusPremium,
        int threads,
        IProgress<UmoEvent>? onEvent = null,
        CancellationToken ct = default)
    {
        var failed = new List<string>();
        var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var errorLines = new List<string>();
        string? lastFailedMod = null;
        int? lastCurrent = null, lastTotal = null;
        var gate = new object();

        // SyncProgress, deliberately: umo prints its failure summary as the
        // very last lines before exit, and Progress<T>'s async posting would
        // let RunAsync return before those reports land.
        var lineProgress = new SyncProgress<OutputLine>(line =>
        {
            var evt = UmoProgressParser.Parse(line.Text);
            lock (gate)
            {
                if (evt is { Kind: UmoEventKind.ModFailed, ModName: { } name })
                {
                    failed.Add(name);
                    lastFailedMod = name;
                    // "No mod file found for …" is header and reason in one line.
                    if (evt.RawLine.StartsWith("No mod file found", StringComparison.OrdinalIgnoreCase))
                        reasons.TryAdd(name, UmoProgressParser.NormalizeError(evt.RawLine));
                }
                else if (evt.Kind == UmoEventKind.ErrorDetail
                         && UmoProgressParser.NormalizeError(evt.RawLine) is { Length: > 0 } detail)
                {
                    if (errorLines.Count < 2000)
                        errorLines.Add(detail);
                    if (lastFailedMod is { } mod)
                        reasons.TryAdd(mod, detail);
                }
                if (evt.Current is not null)
                    (lastCurrent, lastTotal) = (evt.Current, evt.Total);
            }
            onEvent?.Report(evt);
        });

        var args = new List<string> { "install", "--sync", listName, "--no-gui", "--verbose" };
        args.AddRange(nexusPremium ? ["--nexus-premium"] : ["--no-nexus-premium"]);
        args.AddRange(["--threads", threads.ToString()]);

        var result = await runner.RunAsync(new ProcessSpec
        {
            Exe = UmoExe,
            Args = args,
            Env = BaseEnv,
        }, lineProgress, ct).ConfigureAwait(false);

        lock (gate)
            return new UmoInstallResult(
                result, failed.ToList(), new Dictionary<string, string>(reasons, StringComparer.OrdinalIgnoreCase),
                errorLines.ToList(), lastCurrent, lastTotal);
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        var lines = new List<string>();
        var progress = new SyncProgress<OutputLine>(l =>
        {
            lock (lines)
                lines.Add(l.Text);
        });

        try
        {
            var result = await runner.RunAsync(new ProcessSpec
            {
                Exe = UmoExe,
                Args = ["--version"],
                Env = BaseEnv,
                Timeout = TimeSpan.FromSeconds(60),
            }, progress, ct).ConfigureAwait(false);

            if (!result.Success)
                return null;

            lock (lines)
                return lines.FirstOrDefault()?.Trim();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or TimeoutException)
        {
            // Missing binary (AV quarantine) or a hung start — both mean "not usable".
            return null;
        }
    }
}
