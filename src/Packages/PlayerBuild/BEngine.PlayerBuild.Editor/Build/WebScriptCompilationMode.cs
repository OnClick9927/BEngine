namespace BEngine.Editor;

/// <summary>Compilation mode for project and extension scripts in a Web Player.</summary>
public enum WebScriptCompilationMode
{
    /// <summary>Keep runtime-loaded script assemblies interpreted.</summary>
    Interpreter = 0,
    /// <summary>Precompile bundled scripts; code updates require rebuilding the Web main package.</summary>
    AheadOfTime = 1
}
