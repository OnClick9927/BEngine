using System.Reflection;
using BEngine.Editor;
using BEngine.Editor.Codex;

namespace BEngine.ExampleTests.CodexCliDiscovery;

internal static class BEngineSkillCatalogTests
{
    internal static void Run()
    {
        using var fixture = new CodexCliFixture("skills");
        var coreRoot = Path.Combine(fixture.Root, "Core");
        var coreSkill = WriteSkill(coreRoot, "bengine-core", "CORE_SKILL_BODY");
        EditorResources.RegisterResourceRoot(coreRoot);

        var packageRoot = Path.Combine(fixture.ProjectRoot, "Packages", "com.bengine.example");
        var packageSkill = WriteSkill(packageRoot, "bengine-example", "PACKAGE_SKILL_BODY");
        var ignoredSkill = WriteSkill(Path.Combine(fixture.ProjectRoot, "Assets"), "ignored", "IGNORED_SKILL_BODY");

        using var client = new CodexAppServerClient(fixture.ProjectRoot);
        var prompt = InvokeBuildPrompt(client, "Implement the feature");

        TestAssert.True(prompt.Contains(Path.GetFullPath(coreSkill), StringComparison.OrdinalIgnoreCase),
            "The built-in Core skill was not included in the Codex prompt.");
        TestAssert.True(prompt.Contains(
                Path.GetRelativePath(fixture.ProjectRoot, packageSkill).Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase),
            "The enabled project package skill was not included in the Codex prompt.");
        TestAssert.True(!prompt.Contains(ignoredSkill, StringComparison.OrdinalIgnoreCase),
            "A skill outside EditorResources/Skills was included in the Codex prompt.");
        TestAssert.True(!prompt.Contains("CORE_SKILL_BODY", StringComparison.Ordinal) &&
                        !prompt.Contains("PACKAGE_SKILL_BODY", StringComparison.Ordinal),
            "Skill contents were eagerly injected instead of being loaded only when relevant.");
    }

    private static string WriteSkill(string resourceRoot, string skillName, string body)
    {
        var path = Path.Combine(resourceRoot, "EditorResources", "Skills", skillName, "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, body);
        return path;
    }

    private static string InvokeBuildPrompt(CodexAppServerClient client, string prompt)
    {
        var method = typeof(CodexAppServerClient).GetMethod(
            "BuildPrompt", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(CodexAppServerClient).FullName, "BuildPrompt");
        return (string)(method.Invoke(client, [prompt, Array.Empty<string>()])
                        ?? throw new InvalidOperationException("BuildPrompt returned null."));
    }
}
