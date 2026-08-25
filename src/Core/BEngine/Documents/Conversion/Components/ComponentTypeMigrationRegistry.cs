namespace BEngine.Documents;

public static class ComponentTypeMigrationRegistry
{
    private static readonly Dictionary<string, string> ExactMappings = new(StringComparer.Ordinal);
    private static readonly List<(string Source, string Target)> PrefixMappings = [];

    public static void Register(string legacyTypeName, string currentTypeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentTypeName);
        ExactMappings[legacyTypeName] = currentTypeName;
    }

    public static void RegisterPrefix(string legacyPrefix, string currentPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPrefix);
        PrefixMappings.RemoveAll(mapping => mapping.Source == legacyPrefix);
        PrefixMappings.Add((legacyPrefix, currentPrefix));
    }

    internal static string[] GetCandidates(string typeName)
    {
        var candidates = new List<string>();
        if (ExactMappings.TryGetValue(typeName, out var exact)) candidates.Add(exact);
        candidates.AddRange(PrefixMappings
            .Where(mapping => typeName.StartsWith(mapping.Source, StringComparison.Ordinal))
            .Select(mapping => mapping.Target + typeName[mapping.Source.Length..]));
        return [.. candidates.Distinct(StringComparer.Ordinal)];
    }
}
