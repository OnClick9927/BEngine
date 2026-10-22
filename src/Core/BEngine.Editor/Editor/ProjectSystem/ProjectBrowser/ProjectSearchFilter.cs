namespace BEngine.Editor;

internal sealed class ProjectSearchFilter
{
    private readonly string[] _terms;
    private readonly string[] _types;
    private readonly string[] _excludedTerms, _paths, _excludedPaths, _excludedTypes;

    private ProjectSearchFilter(string[] terms, string[] types, string[] excludedTerms, string[] paths, string[] excludedPaths, string[] excludedTypes)
        => (_terms, _types, _excludedTerms, _paths, _excludedPaths, _excludedTypes) = (terms, types, excludedTerms, paths, excludedPaths, excludedTypes);

    internal static ProjectSearchFilter Parse(string? value)
    {
        var terms = new List<string>();
        var types = new List<string>();
        var excludedTerms = new List<string>(); var paths = new List<string>(); var excludedPaths = new List<string>(); var excludedTypes = new List<string>();
        foreach (var original in Tokenize(value))
        {
            var excluded = original.StartsWith('-') && original.Length > 1;
            var token = excluded ? original[1..] : original;
            if (token.StartsWith("t:", StringComparison.OrdinalIgnoreCase) && token.Length > 2)
                (excluded ? excludedTypes : types).Add(token[2..]);
            else if (token.StartsWith("p:", StringComparison.OrdinalIgnoreCase) && token.Length > 2)
                (excluded ? excludedPaths : paths).Add(token[2..].Replace('\\', '/'));
            else (excluded ? excludedTerms : terms).Add(token);
        }
        return new ProjectSearchFilter(terms.ToArray(), types.ToArray(), excludedTerms.ToArray(), paths.ToArray(), excludedPaths.ToArray(), excludedTypes.ToArray());
    }

    internal bool Matches(ProjectBrowserItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_types.Length > 0 && !_types.Any(type => MatchesType(item, type))) return false;
        if (_excludedTypes.Any(type => MatchesType(item, type))) return false;
        var path = item.NormalizedPath;
        if (!_paths.All(term => path.Contains(term, StringComparison.OrdinalIgnoreCase)) || _excludedPaths.Any(term => path.Contains(term, StringComparison.OrdinalIgnoreCase))) return false;
        var displayName = item.EffectiveDisplayName;
        bool Contains(string term) => displayName.Contains(term, StringComparison.OrdinalIgnoreCase) || path.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            item.AssetType.Contains(term, StringComparison.OrdinalIgnoreCase) || item.PackageId?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
        return _terms.All(Contains) && !_excludedTerms.Any(Contains);
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
