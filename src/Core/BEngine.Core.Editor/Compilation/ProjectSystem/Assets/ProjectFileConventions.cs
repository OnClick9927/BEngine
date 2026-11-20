namespace BEngine.ProjectSystem.Editor;

/// <summary>Shared project metadata and atomic-write naming conventions used by import, watching and copying.</summary>
public static class ProjectFileConventions
{
    // Metadata locks and atomic-write files are implementation details, never assets.
    public static bool IsAuxiliaryPath(string path)
    {
        var name = path.AsSpan();
        name = name[(name.LastIndexOfAny('/', '\\') + 1)..];
        return name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ||
               name.Contains(".meta.lock", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith('~') ||
               name.StartsWith('.') && name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }
}
