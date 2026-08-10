namespace Mri.Core.OpenMw;

/// <summary>
/// Overlays the shipped tuned template onto the user's settings.cfg:
/// only keys present in the template are (re)written; every other setting the
/// user already had is preserved untouched.
/// </summary>
public static class SettingsCfgMerger
{
    public static MergeResult Merge(string existingText, string templateText)
    {
        var doc = IniDocument.Parse(existingText);
        var changes = 0;

        foreach (var (section, key, value) in IniDocument.Parse(templateText).Entries())
            if (doc.Set(section, key, value))
                changes++;

        return new MergeResult(doc.ToText(), changes);
    }

    /// <summary>True when every template key already has the template value.</summary>
    public static bool IsApplied(string existingText, string templateText)
    {
        var doc = IniDocument.Parse(existingText);
        return IniDocument.Parse(templateText).Entries()
            .All(e => doc.Get(e.Section, e.Key) == e.Value);
    }
}

public sealed record MergeResult(string Text, int ChangedKeys);
