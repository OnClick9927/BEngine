namespace BEngine.ProjectSystem.Editor;

/// <summary>Compilation input conventions shared by source monitors and editor hosts.</summary>
public static class ProjectCompilationInputs
{
    public static bool IsScriptPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".asmdef.yaml", StringComparison.OrdinalIgnoreCase);
    }
}
