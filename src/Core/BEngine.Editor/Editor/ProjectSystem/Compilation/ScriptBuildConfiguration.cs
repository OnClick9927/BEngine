namespace BEngine.ProjectSystem;

internal readonly record struct ScriptBuildConfiguration(
    string TargetFramework,
    RuntimePlatform Platform,
    IReadOnlyList<string> DefineSymbols,
    bool IsEditor);
