namespace BEngine.Editor.Documents;

internal static class EditorDataValidation
{
    internal static void ValidateAssemblyDefinition(AssemblyDefinitionDocument document)
    {
        if (document.Format != "BEngine.AssemblyDefinition" || document.Version != 1)
            throw new InvalidDataException(
                $"Unsupported assembly definition '{document.Format}' v{document.Version}.");
        if (string.IsNullOrWhiteSpace(document.Name) ||
            document.Name != document.Name.Trim() ||
            document.Name.IndexOfAny(EditorHostServices.Paths.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Assembly definition name is invalid.");
        if (!IsNamespace(document.RootNamespace))
            throw new InvalidDataException("Root namespace is invalid.");
        ValidateDistinct(document.References, "reference", document.Name, StringComparer.OrdinalIgnoreCase);
        if (document.References.Contains(document.Name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Assembly definition '{document.Name}' cannot reference itself.");
        ValidateDistinct(document.IncludePlatforms, "included platform", document.Name,
            StringComparer.OrdinalIgnoreCase);
        ValidateDistinct(document.ExcludePlatforms, "excluded platform", document.Name,
            StringComparer.OrdinalIgnoreCase);
        if (document.IncludePlatforms.Count > 0 && document.ExcludePlatforms.Count > 0)
            throw new InvalidDataException(
                "Platforms cannot be both included and excluded; configure only one platform list.");
        ValidateDistinct(document.DefineConstraints, "define constraint", document.Name, StringComparer.Ordinal);
        foreach (var constraint in document.DefineConstraints)
        {
            var symbol = constraint.StartsWith('!') ? constraint[1..] : constraint;
            if (!IsIdentifier(symbol))
                throw new InvalidDataException($"Invalid define constraint '{constraint}'.");
        }
    }

    private static void ValidateDistinct(
        IReadOnlyCollection<string> values,
        string kind,
        string assemblyName,
        StringComparer comparer)
    {
        if (values.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException($"Assembly definition '{assemblyName}' contains an empty {kind}.");
        if (((((global::System.Collections.Generic.IEnumerable<string>)(values.Select(value => value.Trim()))), (global::System.Collections.Generic.IEqualityComparer<string>? )(comparer)) switch
        {
            (string[] { Length: 0 }, _) => 0,
            var (distinctItems2440, distinctComparer2440) => distinctItems2440.ToHashSet<string>(distinctComparer2440).Count
        }) != values.Count)
            throw new InvalidDataException($"Assembly definition '{assemblyName}' contains a duplicate {kind}.");
    }

    private static bool IsNamespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.AsSpan();
        foreach (var range in text.Split('.'))
            if (!IsIdentifier(text[range])) return false;
        return true;
    }

    private static bool IsIdentifier(string value) => IsIdentifier(value.AsSpan());

    private static bool IsIdentifier(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || !(char.IsLetter(value[0]) || value[0] == '_')) return false;
        foreach (var character in value[1..])
            if (!(char.IsLetterOrDigit(character) || character == '_')) return false;
        return true;
    }

}
