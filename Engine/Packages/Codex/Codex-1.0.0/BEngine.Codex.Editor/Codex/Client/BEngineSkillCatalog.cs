namespace BEngine.Editor.Codex;

internal static class BEngineSkillCatalog
{
    private const string SkillFileName = "SKILL.md";

    internal static IReadOnlyList<string> Discover(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var fullProjectRoot = EditorHostServices.Paths.GetFullPath(projectRoot);
        var skills = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        if (EditorResource.FindPath($"Skills/bengine-core/{SkillFileName}") is { } coreSkill)
            skills.Add(EditorHostServices.Paths.GetFullPath(coreSkill));

        var packagesRoot = EditorHostServices.Paths.Combine(fullProjectRoot, "Library", "Packages");
        if (!EditorHostServices.FileSystem.DirectoryExists(packagesRoot)) return skills.ToArray();

        foreach (var packageRoot in EditorHostServices.FileSystem.EnumerateDirectories(packagesRoot, "*", SearchOption.TopDirectoryOnly))
        {
            foreach (var skillsRoot in new[]
                     {
                         EditorHostServices.Paths.Combine(packageRoot, "Skills"),
                         EditorHostServices.Paths.Combine(packageRoot, EditorResource.DirectoryName, "Skills")
                     })
            {
                if (!EditorHostServices.FileSystem.DirectoryExists(skillsRoot)) continue;
                foreach (var skillFile in EditorHostServices.FileSystem.EnumerateFiles(skillsRoot, SkillFileName, SearchOption.AllDirectories))
                    skills.Add(EditorHostServices.Paths.GetRelativePath(fullProjectRoot, skillFile).Replace('\\', '/'));
            }
        }

        return skills.ToArray();
    }
}
