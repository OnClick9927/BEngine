namespace BEngine.Editor.Build;

/// <summary>Keeps the quick-development build profile aligned with compiler settings.</summary>
public sealed class PlayerBuildScriptingDefinePersistence(string projectPath) : IEditorScriptingDefinePersistence
{
    public void Save(IReadOnlyList<string> symbols)
    {
        var settings = PlayerBuildSettingsStore.Load(projectPath);
        if (settings.ScriptingDefineSymbols.SequenceEqual(symbols, StringComparer.Ordinal)) return;
        settings.ScriptingDefineSymbols = symbols.ToList();
        PlayerBuildSettingsStore.Save(projectPath, settings);
    }
}
