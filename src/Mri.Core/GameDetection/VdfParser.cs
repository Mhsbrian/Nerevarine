namespace Mri.Core.GameDetection;

/// <summary>
/// A node in Valve's KeyValues text format (libraryfolders.vdf,
/// appmanifest_*.acf). Keys are case-insensitive, matching Valve's own readers.
/// </summary>
public sealed class VdfNode
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? GetValue(string key) => Values.GetValueOrDefault(key);

    public VdfNode? GetChild(string key) => Children.GetValueOrDefault(key);
}

/// <summary>
/// Parser for Valve's KeyValues text format: quoted or bare tokens, nested
/// braces, // comments, and backslash escapes inside quoted strings.
/// </summary>
public static class VdfParser
{
    public static VdfNode Parse(string text)
    {
        var pos = 0;
        var rootChildren = new VdfNode();
        // A .vdf file is a sequence of "name" { ... } pairs at top level
        // (almost always exactly one). Wrap them in a synthetic root.
        while (ReadToken(text, ref pos) is { } key)
        {
            SkipWhitespaceAndComments(text, ref pos);
            if (pos < text.Length && text[pos] == '{')
            {
                pos++;
                rootChildren.Children[key] = ParseObject(text, ref pos);
            }
            else if (ReadToken(text, ref pos) is { } value)
            {
                rootChildren.Values[key] = value;
            }
            else
            {
                throw new FormatException($"VDF: key '{key}' has no value.");
            }
        }

        return rootChildren;
    }

    private static VdfNode ParseObject(string text, ref int pos)
    {
        var node = new VdfNode();
        while (true)
        {
            SkipWhitespaceAndComments(text, ref pos);
            if (pos >= text.Length)
                throw new FormatException("VDF: unexpected end of input inside object.");
            if (text[pos] == '}')
            {
                pos++;
                return node;
            }

            var key = ReadToken(text, ref pos)
                ?? throw new FormatException("VDF: expected key inside object.");

            SkipWhitespaceAndComments(text, ref pos);
            if (pos < text.Length && text[pos] == '{')
            {
                pos++;
                node.Children[key] = ParseObject(text, ref pos);
            }
            else
            {
                node.Values[key] = ReadToken(text, ref pos)
                    ?? throw new FormatException($"VDF: key '{key}' has no value.");
            }
        }
    }

    private static string? ReadToken(string text, ref int pos)
    {
        SkipWhitespaceAndComments(text, ref pos);
        if (pos >= text.Length || text[pos] is '{' or '}')
            return null;

        if (text[pos] == '"')
        {
            pos++;
            var sb = new System.Text.StringBuilder();
            while (pos < text.Length && text[pos] != '"')
            {
                if (text[pos] == '\\' && pos + 1 < text.Length)
                {
                    pos++;
                    sb.Append(text[pos] switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        '\\' => '\\',
                        '"' => '"',
                        var c => c,
                    });
                }
                else
                {
                    sb.Append(text[pos]);
                }
                pos++;
            }

            if (pos >= text.Length)
                throw new FormatException("VDF: unterminated quoted string.");
            pos++; // closing quote
            return sb.ToString();
        }

        var start = pos;
        while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] is not ('{' or '}' or '"'))
            pos++;
        return text[start..pos];
    }

    private static void SkipWhitespaceAndComments(string text, ref int pos)
    {
        while (pos < text.Length)
        {
            if (char.IsWhiteSpace(text[pos]))
            {
                pos++;
            }
            else if (text[pos] == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
            {
                while (pos < text.Length && text[pos] != '\n')
                    pos++;
            }
            else
            {
                break;
            }
        }
    }
}
