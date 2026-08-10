namespace Mri.Core.OpenMw;

/// <summary>
/// Order-preserving line model of an openmw.cfg: repeated keys are the norm
/// (data=, content=, fallback=...), unknown lines survive verbatim.
/// </summary>
public sealed class OpenMwCfg
{
    public List<string> Lines { get; } = [];

    public static OpenMwCfg Parse(string text)
    {
        var cfg = new OpenMwCfg();
        cfg.Lines.AddRange(text.Split('\n').Select(l => l.TrimEnd('\r')));
        // A trailing newline produces one empty tail entry; drop it so
        // ToText round-trips cleanly.
        if (cfg.Lines.Count > 0 && cfg.Lines[^1].Length == 0)
            cfg.Lines.RemoveAt(cfg.Lines.Count - 1);
        return cfg;
    }

    /// <summary>All values of a (possibly repeated) key, in file order.</summary>
    public IReadOnlyList<string> GetValues(string key)
    {
        var prefix = key + "=";
        return Lines
            .Where(l => l.StartsWith(prefix, StringComparison.Ordinal))
            .Select(l => l[prefix.Length..])
            .ToList();
    }

    public void RemoveAll(params string[] keys)
    {
        var prefixes = keys.Select(k => k + "=").ToArray();
        Lines.RemoveAll(l => prefixes.Any(p => l.StartsWith(p, StringComparison.Ordinal)));
    }

    public string ToText() => string.Join("\n", Lines) + "\n";
}
