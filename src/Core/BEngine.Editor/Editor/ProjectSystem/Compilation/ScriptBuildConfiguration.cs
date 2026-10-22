namespace BEngine.ProjectSystem;

internal readonly record struct ScriptBuildConfiguration(
    string TargetFramework,
    RuntimePlatform Platform,
    IReadOnlyList<string> DefineSymbols,
    bool IsEditor,
    string Configuration = "Debug",
    bool IsPlayerBuild = false);
