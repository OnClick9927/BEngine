using BEngine.Serialization;
using BEngine.Documents;
using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem.Editor;

public static class ProjectWorkspaceFactory
{
    public static ProjectWorkspace Create(string rootPath, string projectName) =>
        Create(rootPath, projectName, Array.Empty<string>());

    public static ProjectWorkspace Create(
        string rootPath,
        string projectName,
        IEnumerable<string> packageIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentNullException.ThrowIfNull(packageIds);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (File.Exists(root)) throw new IOException($"The project path is a file: {root}.");
        var targetExisted = Directory.Exists(root);
        if (targetExisted && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException($"The project folder is not empty: {root}.");
        var parent = Directory.GetParent(root) ??
                     throw new IOException($"The project folder must have a parent directory: {root}.");
        Directory.CreateDirectory(parent.FullName);
        var requestedPackageIds = packageIds.Where(packageId => !string.IsNullOrWhiteSpace(packageId))
            .Select(packageId => packageId.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var stagingRoot = Path.Combine(parent.FullName, $".bengine-project-{Guid.NewGuid():N}");
        try
        {
            CreateWorkspaceContents(stagingRoot, projectName.Trim(), requestedPackageIds);
            if (File.Exists(root)) throw new IOException($"The project path became a file: {root}.");
            if (Directory.Exists(root))
            {
                if (!targetExisted || Directory.EnumerateFileSystemEntries(root).Any())
                    throw new IOException($"The project folder changed while the project was being created: {root}.");
                Directory.Delete(root);
            }
            Directory.Move(stagingRoot, root);
            return ProjectWorkspace.Open(root);
        }
        finally
        {
            if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, true);
        }
    }

    private static void CreateWorkspaceContents(
        string root,
        string projectName,
        IReadOnlyList<string> packageIds)
    {
        var trimmedName = projectName.Trim();
        var project = new ProjectDocument
        {
            Name = trimmedName,
            Window = new WindowDocument { Title = trimmedName }
        };
        var workspace = new ProjectWorkspace(root, project);
        project.Save(workspace.ProjectFilePath);
        new EditorSettingsDocument().Save(Path.Combine(workspace.ProjectSettingsPath, "EditorSettings.yaml"));
        new EditorLayoutDocument().Save(workspace.EditorLayoutPath);
        new ProjectSettingsDocument
        {
            ProductName = project.Name,
            DefaultScreenWidth = project.Window.Width,
            DefaultScreenHeight = project.Window.Height,
            GraphicsBackend = "Vulkan"
        }.Save(workspace.ProjectSettingsFilePath);

        if (packageIds.Count == 0)
        {
            new PackageManifestDocument().Save(workspace.PackageManifestPath);
        }
        else
        {
            using var packages = new BPackageManager(workspace, null, loadAssemblies: false);
            foreach (var packageId in packageIds) packages.SetEnabled(packageId, true);
        }
        CreateDefaultScene(workspace);
        CreateDefaultScript(workspace);
        CreateDefaultAssemblyDefinition(workspace);
        CreateDefaultEditorScript(workspace);
        CreateIgnoreFile(workspace);
    }

    private static void CreateDefaultScene(ProjectWorkspace workspace)
    {
        var scene = new BEngine.Scene("Main");

        var cameraObject = scene.CreateGameObject("Main Camera");
        cameraObject.AddComponent<BEngine.Camera2D>();

        var sprite = scene.CreateGameObject("Sprite");
        sprite.AddComponent<BEngine.SpriteRenderer>();

        Document.SaveBObject<SceneDocument>(scene, workspace.StartupScenePath);
    }

    private static void CreateDefaultScript(ProjectWorkspace workspace)
    {
        var path = Path.Combine(workspace.ScriptsPath, "Rotator.cs");
        if (File.Exists(path)) return;
        File.WriteAllText(path, """
            using BEngine;

            namespace Game;

            public sealed class Rotator : MonoBehaviour
            {
                public Fix64 degreesPerSecond { get; set; } = 45;

                public override void Update()
                {
                    transform.localRotation += degreesPerSecond * Time.deltaTime;
                }
            }
            """);
    }

    private static void CreateDefaultEditorScript(ProjectWorkspace workspace)
    {
        var path = Path.Combine(workspace.EditorScriptsPath, "ProjectMenus.cs");
        if (File.Exists(path)) return;
        File.WriteAllText(path, """
            using BEngine;
            using BEngine.Editor;

            namespace Game.Editor;

            public static class ProjectMenus
            {
                [MenuItem("Tools/Project/Log Selected Object", false, 100)]
                private static void LogSelectedObject()
                {
                    Debug.Log($"Selected: {Selection.activeGameObject?.name}");
                }

                [MenuItem("Tools/Project/Log Selected Object", true)]
                private static bool ValidateLogSelectedObject() => Selection.activeGameObject is not null;
            }
            """);
    }

    private static void CreateDefaultAssemblyDefinition(ProjectWorkspace workspace)
    {
        var path = Path.Combine(workspace.ScriptsPath, "Game.asmdef.yaml");
        if (File.Exists(path)) return;
        new AssemblyDefinitionDocument().Save(path);
    }

    private static void CreateIgnoreFile(ProjectWorkspace workspace)
    {
        var path = Path.Combine(workspace.RootPath, ".gitignore");
        if (File.Exists(path)) return;
        File.WriteAllText(path, "Library/\nLogs/\nTemp/\n");
    }
}
