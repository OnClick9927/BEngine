namespace BEngine.Editor.Codex;

internal static class BEngineSkillCatalog
{
    private const string SkillFileName = "SKILL.md";

    internal static IReadOnlyList<string> Discover(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var fullProjectRoot = Path.GetFullPath(projectRoot);
        var skills = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        if (EditorResources.FindPath($"Skills/bengine-core/{SkillFileName}") is { } coreSkill)
            skills.Add(Path.GetFullPath(coreSkill));

        var packagesRoot = Path.Combine(fullProjectRoot, "Packages");
        if (!Directory.Exists(packagesRoot)) return skills.ToArray();

        foreach (var packageRoot in Directory.EnumerateDirectories(packagesRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var skillsRoot = Path.Combine(packageRoot, "EditorResources", "Skills");
            if (!Directory.Exists(skillsRoot)) continue;
            foreach (var skillRoot in Directory.EnumerateDirectories(skillsRoot, "*", SearchOption.TopDirectoryOnly))
            {
                var skillFile = Path.Combine(skillRoot, SkillFileName);
                if (!File.Exists(skillFile)) continue;
                skills.Add(Path.GetRelativePath(fullProjectRoot, skillFile).Replace('\\', '/'));
            }
        }

        return skills.ToArray();
    }
}
