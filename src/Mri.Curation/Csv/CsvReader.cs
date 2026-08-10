namespace Mri.Curation.Csv;

/// <summary>Minimal RFC 4180 reader — quoted fields, embedded commas/quotes/newlines.</summary>
public static class CsvReader
{
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;
        var i = 0;

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
        }

        void EndRow()
        {
            EndField();
            rows.Add(fields.ToArray());
            fields.Clear();
        }

        while (i < text.Length)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }
                    inQuotes = false;
                    i++;
                    continue;
                }
                field.Append(c);
                i++;
                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    i++;
                    break;
                case ',':
                    EndField();
                    i++;
                    break;
                case '\r':
                    i++;
                    break;
                case '\n':
                    EndRow();
                    i++;
                    break;
                default:
                    field.Append(c);
                    i++;
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
            EndRow();

        return rows;
    }
}
