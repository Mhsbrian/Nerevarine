using System.Text.Json;

namespace Mri.Core.Logging;

/// <summary>
/// Replaces secrets with a mask — including their common encoded forms.
/// A secret containing '+' appears as "+" inside JSON written by
/// System.Text.Json (its default encoder escapes '+'), and as "%2B" when
/// URL-encoded; a raw-string Replace alone silently leaks those variants —
/// exactly how a real Nexus key slipped through a diagnostics bundle once.
/// </summary>
public static class Redactor
{
    public const string Mask = "«redacted»";

    public static IReadOnlyList<string> VariantsOf(string secret)
    {
        var variants = new List<string> { secret };

        // JSON string-escaped form, quotes stripped (e.g. '+' → '+').
        var jsonEscaped = JsonSerializer.Serialize(secret)[1..^1];
        if (jsonEscaped != secret)
            variants.Add(jsonEscaped);

        var urlEscaped = Uri.EscapeDataString(secret);
        if (urlEscaped != secret)
            variants.Add(urlEscaped);

        return variants;
    }

    public static string Apply(string text, IEnumerable<string> variants)
    {
        foreach (var variant in variants)
            text = text.Replace(variant, Mask);
        return text;
    }
}
