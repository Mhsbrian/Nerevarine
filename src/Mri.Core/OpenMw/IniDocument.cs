namespace Mri.Core.OpenMw;

/// <summary>
/// Section-aware INI editor that preserves layout, comments and spacing —
/// settings.cfg uses "key = value" under [Section] headers. Only the keys we
/// touch change; everything else round-trips byte-identical.
/// </summary>
public sealed class IniDocument
{
    private readonly List<string> _lines;

    private IniDocument(List<string> lines) => _lines = lines;

    public static IniDocument Parse(string text)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return new IniDocument(lines);
    }

    public string? Get(string section, string key)
    {
        var (start, end) = FindSection(section);
        if (start < 0)
            return null;

        for (var i = start + 1; i < end; i++)
            if (TryParseEntry(_lines[i], out var k, out var v) &&
                string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return v;
        return null;
    }

    /// <summary>Returns true if the document changed.</summary>
    public bool Set(string section, string key, string value)
    {
        var (start, end) = FindSection(section);
        if (start < 0)
        {
            if (_lines.Count > 0 && _lines[^1].Length > 0)
                _lines.Add(string.Empty);
            _lines.Add($"[{section}]");
            _lines.Add($"{key} = {value}");
            return true;
        }

        var lastEntry = start;
        for (var i = start + 1; i < end; i++)
        {
            if (TryParseEntry(_lines[i], out var k, out var v))
            {
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (v == value)
                        return false;
                    _lines[i] = $"{key} = {value}";
                    return true;
                }
                lastEntry = i;
            }
        }

        _lines.Insert(lastEntry + 1, $"{key} = {value}");
        return true;
    }

    public IEnumerable<(string Section, string Key, string Value)> Entries()
    {
        var section = string.Empty;
        foreach (var line in _lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                section = trimmed[1..^1];
            else if (TryParseEntry(line, out var key, out var value))
                yield return (section, key, value);
        }
    }

    public string ToText() => string.Join("\n", _lines) + "\n";

    private (int Start, int End) FindSection(string section)
    {
        var start = -1;
        for (var i = 0; i < _lines.Count; i++)
        {
            var trimmed = _lines[i].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                if (start >= 0)
                    return (start, i);
                if (string.Equals(trimmed[1..^1], section, StringComparison.OrdinalIgnoreCase))
                    start = i;
            }
        }
        return start >= 0 ? (start, _lines.Count) : (-1, -1);
    }

    private static bool TryParseEntry(string line, out string key, out string value)
    {
        key = value = string.Empty;
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';') || trimmed.StartsWith('['))
            return false;

        var eq = trimmed.IndexOf('=');
        if (eq <= 0)
            return false;

        key = trimmed[..eq].Trim();
        value = trimmed[(eq + 1)..].Trim();
        return true;
    }
}
