namespace BEngine.Launcher;

internal static class LauncherUpdateWorkspace
{
    // Keep downloaded content on the same volume as the Hub and outside every
    // managed install destination. Temp is protected by both manifest validators.
    internal static string Create(string installationRoot)
    {
        var workspace = Path.Combine(installationRoot, "Temp", "HubUpdates", Guid.NewGuid().ToString("N"));
        Validate(installationRoot, workspace);
        return Path.GetFullPath(workspace);
    }

    internal static void Validate(string installationRoot, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(installationRoot) || string.IsNullOrWhiteSpace(workspaceRoot) ||
            !Path.IsPathFullyQualified(installationRoot) || !Path.IsPathFullyQualified(workspaceRoot))
            throw new InvalidDataException("The installation and workspace must be absolute paths.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationRoot));
        var workspace = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        if (IsWithin(root, workspace))
        {
            var workspaceParent = Path.GetDirectoryName(workspace);
            var expectedParent = Path.Combine(root, "Temp", "HubUpdates");
            if (!string.Equals(workspaceParent, expectedParent, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(workspace), "N", out _))
                throw new InvalidDataException("The update workspace must be a unique directory under this Hub's Temp/HubUpdates folder.");
        }
        // Existing external plans must remain recoverable after this upgrade.
        // An enclosing workspace could include managed files and is never safe.
        else if (IsWithin(workspace, root))
            throw new InvalidDataException("The update workspace cannot contain the installation directory.");
        EnsureNoReparsePoints(root);
        EnsureNoReparsePoints(workspace);
    }

    private static bool IsWithin(string root, string path) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void EnsureNoReparsePoints(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Refusing to follow a symbolic link or junction: {current}");
    }
}
