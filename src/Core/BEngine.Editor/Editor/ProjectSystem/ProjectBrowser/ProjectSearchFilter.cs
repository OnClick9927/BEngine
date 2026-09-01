namespace BEngine.Editor;

internal sealed class ProjectSearchFilter
{
    private readonly string[] _terms;
    private readonly string[] _types;

    private ProjectSearchFilter(string[] terms, string[] types) => (_terms, _types) = (terms, types);

    internal static ProjectSearchFilter Parse(string? value)
    {
        var terms = new List<string>();
        var types = new List<string>();
        foreach (var token in Tokenize(value))
        {
            if (token.StartsWith("t:", StringComparison.OrdinalIgnoreCase) && token.Length > 2)
                types.Add(token[2..]);
            else terms.Add(token);
        }
        return new ProjectSearchFilter(terms.ToArray(), types.ToArray());
    }

    internal bool Matches(ProjectBrowserItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_types.Length > 0 && !_types.Any(type => MatchesType(item, type))) return false;
        return _terms.All(term => item.EffectiveDisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                  item.NormalizedPath.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                  item.AssetType.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                  item.PackageId?.Contains(term, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool MatchesType(ProjectBrowserItem item, string requested)
    {
        var type = requested.Trim();
        if (type.Equals("Folder", StringComparison.OrdinalIgnoreCase)) return item.IsDirectory;
        if (item.IsDirectory) return false;
        if (type.Equals("GameObject", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("PrefabAsset", StringComparison.OrdinalIgnoreCase))
            return item.AssetType.Equals("Prefab", StringComparison.OrdinalIgnoreCase);
        if (type.Equals(nameof(MonoScript), StringComparison.OrdinalIgnoreCase)) type = nameof(Script);
        if (type.Equals(nameof(Scene) + "Asset", StringComparison.OrdinalIgnoreCase)) type = nameof(Scene);
        if (type.Equals("Texture", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("Texture2D", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("Image", StringComparison.OrdinalIgnoreCase))
            return item.AssetType is "Texture" or "Image";
        return item.AssetType.Equals(type, StringComparison.OrdinalIgnoreCase) ||
               item.AssetType.Replace(" ", string.Empty, StringComparison.Ordinal)
                   .Equals(type.Replace(" ", string.Empty, StringComparison.Ordinal),
                       StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Tokenize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;
        var buffer = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var character in value.Trim())
        {
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (buffer.Length == 0) continue;
                yield return buffer.ToString();
                buffer.Clear();
                continue;
            }
            buffer.Append(character);
        }
        if (buffer.Length > 0) yield return buffer.ToString();
    }
}
