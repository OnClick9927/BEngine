namespace BEngine.ProjectSystem.Editor;

internal static class ProjectSourceChangeClassifier
{
    internal static bool IsScriptPath(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".asmdef.yaml", StringComparison.OrdinalIgnoreCase);
}
