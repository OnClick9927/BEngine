using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
namespace BEngine.Editor;

/// <summary>The shared edit operation used by scripting and workflow settings.</summary>
public static class EditorScriptingDefines
{
    public static IReadOnlyList<string> symbols =>
        EditorProjectSettings.current.ScriptingDefineSymbols.AsReadOnly();

    public static bool SetSymbols(IEnumerable<string> symbols, bool requestCompilation = true)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        var normalized = ((((global::System.Collections.Generic.IEnumerable<string>)(symbols.Select(static symbol => symbol?.Trim() ?? string.Empty))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.Ordinal)) switch
        {
            (string[] { Length: 0 }, _) => new global::System.Collections.Generic.List<string>(),
            var (distinctItems478, distinctComparer478) => distinctItems478.ToHashSet<string>(distinctComparer478).ToList()});
        foreach (var symbol in normalized)
            if (symbol.Length == 0 || !(char.IsLetter(symbol[0]) || symbol[0] == '_') ||
                symbol.Skip(1).Any(static character => !(char.IsLetterOrDigit(character) || character == '_')))
                throw new InvalidDataException($"Invalid scripting define symbol '{symbol}'.");
        if (EditorProjectSettings.current.ScriptingDefineSymbols.SequenceEqual(normalized, StringComparer.Ordinal))
            return false;

        // A workflow can persist the same symbols in its own profile, without
        // teaching the compiler/settings mechanisms about that profile's format.
        var persistence = EditorCapabilities.GetService<IEditorScriptingDefinePersistence>();
        var previous = EditorProjectSettings.current.ScriptingDefineSymbols;
        var projectSaveAttempted = false;
        try
        {
            persistence?.Save(normalized.AsReadOnly());
            EditorProjectSettings.current.ScriptingDefineSymbols = normalized;
            projectSaveAttempted = true;
            EditorProjectSettings.Save();
        }
        catch (Exception failure)
        {
            EditorProjectSettings.current.ScriptingDefineSymbols = previous;
            List<Exception>? rollbackFailures = null;
            // A failed write must not leave a workflow profile using different
            // symbols from the compiler's restored project settings.
            try { persistence?.Save(previous.AsReadOnly()); }
            catch (Exception rollbackFailure) { (rollbackFailures ??= []).Add(rollbackFailure); }
            if (projectSaveAttempted)
            {
                try { EditorProjectSettings.Save(); }
                catch (Exception rollbackFailure) { (rollbackFailures ??= []).Add(rollbackFailure); }
            }
            if (rollbackFailures is { Count: > 0 })
                throw new AggregateException("Scripting symbols and persistence rollback failed.",
                    [failure, .. rollbackFailures]);
            throw;
        }
        if (requestCompilation) CompilationPipeline.RequestScriptCompilation();
        return true;
    }
}
