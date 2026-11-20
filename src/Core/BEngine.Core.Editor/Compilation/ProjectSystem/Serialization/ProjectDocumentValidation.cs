namespace BEngine.Serialization;
using BEngine.ProjectSystem;
internal static class ProjectDocumentValidation
{
    internal static void ValidateProject(ProjectData document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Format != "BEngine.Project" || document.Version != 1)
            throw new InvalidDataException($"Unsupported project document '{document.Format}' v{document.Version}.");
        RuntimeConfiguration.Validate(document);
    }

    internal static void ValidateProjectSettings(ProjectSettingsData document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Format != "BEngine.ProjectSettings" || document.Version != 3)
            throw new InvalidDataException(
                $"Unsupported project settings '{document.Format}' v{document.Version}.");
        RuntimeConfiguration.Validate(document);
        if (document.ScriptingDefineSymbols is null || Query968(document.ScriptingDefineSymbols) || ((((global::System.Collections.Generic.IEnumerable<string>)(document.ScriptingDefineSymbols)), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.Ordinal)) switch
        {
            (string[] { Length: 0 }, _) => 0,
            var (distinctItems1061, distinctComparer1061) => distinctItems1061.ToHashSet<string>(distinctComparer1061).Count
        }) != document.ScriptingDefineSymbols.Count)
            throw new InvalidDataException("Scripting define symbols must be valid and unique.");
        [global::BEngine.SkipLoopGuard]
        bool Query968(global::System.Collections.Generic.IEnumerable<string> querySource968)
        {
            ArgumentNullException.ThrowIfNull(querySource968, "source");
            bool Matches968(string symbol) => !IsValidSymbol(symbol);
            global::System.ReadOnlySpan<string> queryValues968;
            if (querySource968.GetType() == typeof(string[]))
                queryValues968 = (string[])querySource968;
            else if (querySource968.GetType() == typeof(global::System.Collections.Generic.List<string>))
                queryValues968 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)querySource968);
            else
            {
                foreach (var symbol in querySource968)
                    if (Matches968(symbol))
                        return true;
                return false;
            }
        
            foreach (var symbol in queryValues968)
                if (Matches968(symbol))
                    return true;
            return false;
        }
    }

    private static bool IsValidSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) || !(char.IsLetter(symbol[0]) || symbol[0] == '_')) return false;
        foreach (var character in symbol.AsSpan(1))
            if (!(char.IsLetterOrDigit(character) || character == '_')) return false;
        return true;
    }

}
