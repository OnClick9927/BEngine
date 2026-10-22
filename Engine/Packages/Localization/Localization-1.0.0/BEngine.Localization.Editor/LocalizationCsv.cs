using System.Text;

namespace BEngine.Localization.Editor;

/// <summary>RFC 4180 CSV: key,language,text,comment. Empty text is preserved; duplicate keys are rejected.</summary>
public static class LocalizationCsv
{
    public static string Export(IEnumerable<LocalizationTable> tables)
    {
        var result = new StringBuilder("key,language,text,comment\r\n");
        foreach (var table in tables.OrderBy(t => t.language, StringComparer.Ordinal))
        {
            table.Validate();
            foreach (var entry in table.entries.OrderBy(e => e.key, StringComparer.Ordinal))
            {
                if (entry.text is null) continue;
                result.AppendJoin(',', new[] { entry.key, table.language, entry.text, entry.comment }.Select(Quote)).Append("\r\n");
            }
        }
        return result.ToString();
    }

    public static IReadOnlyList<LocalizationTable> Import(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);
        var rows = Parse(csv.TrimStart('\uFEFF'));
        if (rows.Count == 0 || !rows[0].SequenceEqual(new[] { "key", "language", "text", "comment" }))
            throw new InvalidDataException("CSV header must be key,language,text,comment.");
        var tables = new Dictionary<string, LocalizationTable>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var row in rows.Skip(1))
            {
                if (row.Length == 1 && row[0].Length == 0) continue;
                if (row.Length != 4) throw new InvalidDataException("A CSV row must contain four columns.");
                var language = LocalizationContext.NormalizeLanguage(row[1]);
                if (!tables.TryGetValue(language, out var table))
                    tables.Add(language, table = new LocalizationTable { language = language });
                table.entries.Add(new LocalizationEntry { key = row[0], text = row[2], comment = row[3] });
            }
            foreach (var table in tables.Values) table.Validate();
            return tables.Values.ToArray();
        }
        catch
        {
            foreach (var table in tables.Values) BObject.DestroyImmediate(table);
            throw;
        }
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "\"\"") + '"';
    internal static List<string[]> Parse(string csv)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var closed = false;
        for (var index = 0; index < csv.Length; index++)
        {
            var ch = csv[index];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (index + 1 < csv.Length && csv[index + 1] == '"') { cell.Append('"'); index++; }
                    else { quoted = false; closed = true; }
                }
                else cell.Append(ch);
                continue;
            }
            if (ch == '"' && cell.Length == 0 && !closed) { quoted = true; continue; }
            if (ch is ',' or '\r' or '\n')
            {
                row.Add(cell.ToString()); cell.Clear(); closed = false;
                if (ch != ',')
                {
                    rows.Add(row.ToArray()); row.Clear();
                    if (ch == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n') index++;
                }
            }
            else
            {
                if (closed || ch == '"') throw new InvalidDataException("Unexpected data after a quoted CSV value.");
                cell.Append(ch);
            }
        }
        if (quoted) throw new InvalidDataException("Unclosed CSV quotation.");
        if (cell.Length > 0 || row.Count > 0 || closed) { row.Add(cell.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }
}
