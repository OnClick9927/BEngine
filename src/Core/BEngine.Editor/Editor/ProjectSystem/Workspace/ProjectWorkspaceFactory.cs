using BEngine.Serialization;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Platform;

namespace BEngine.ProjectSystem.Editor;

public static class ProjectWorkspaceFactory
{
    private static FileUtility FileSystem => EditorServiceAccess.GetRequiredService<FileUtility>();
    private static PathUtility Paths => EditorServiceAccess.GetRequiredService<PathUtility>();

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

        var root = Paths.TrimEndingDirectorySeparator(Paths.GetFullPath(rootPath));
        if (FileSystem.FileExists(root)) throw new IOException($"The project path is a file: {root}.");
        var targetExisted = FileSystem.DirectoryExists(root);
        if (targetExisted && FileSystem.EnumerateFileSystemEntries(root).Any())
            throw new IOException($"The project folder is not empty: {root}.");
        var parent = Paths.GetDirectoryName(root) ??
                     throw new IOException($"The project folder must have a parent directory: {root}.");
        FileSystem.CreateDirectory(parent);
        var requestedPackageIds = packageIds.Where(packageId => !string.IsNullOrWhiteSpace(packageId))
            .Select(packageId => packageId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var stagingRoot = Paths.Combine(parent, $".bengine-project-{Guid.NewGuid():N}");
        try
        {
            CreateWorkspaceContents(stagingRoot, projectName.Trim(), requestedPackageIds);
            if (FileSystem.FileExists(root)) throw new IOException($"The project path became a file: {root}.");
            if (FileSystem.DirectoryExists(root))
            {
                if (!targetExisted || FileSystem.EnumerateFileSystemEntries(root).Any())
                    throw new IOException($"The project folder changed while the project was being created: {root}.");
                FileSystem.DeleteDirectory(root);
            }
            FileSystem.MoveDirectory(stagingRoot, root);
            return ProjectWorkspace.Open(root, FileSystem, Paths);
        }
        finally
        {
            if (FileSystem.DirectoryExists(stagingRoot)) FileSystem.DeleteDirectory(stagingRoot, true);
        }
    }

    private static void CreateWorkspaceContents(
        string root,
        string projectName,
        IReadOnlyList<string> packageIds)
    {
        var trimmedName = projectName.Trim();
        var project = new ProjectData
        {
            Name = trimmedName,
            EngineVersion = EngineVersion.Current,
            Window = new WindowData { Title = trimmedName }
        };
        var workspace = new ProjectWorkspace(root, project, FileSystem, Paths);
        project.Save(workspace.ProjectFilePath);
        new EditorSettingsDocument().Save(Paths.Combine(workspace.ProjectSettingsPath, "EditorSettings.yaml"));
        new ProjectSettingsData
        {
            ProductName = project.Name,
            DefaultScreenWidth = project.Window.Width,
            DefaultScreenHeight = project.Window.Height,
            GraphicsBackend = "Auto"
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
        foreach (var type in TypeCache.GetTypesDerivedFrom<IProjectTemplate>().Where(t => !t.IsAbstract && !t.IsInterface))
            ((IProjectTemplate)Activator.CreateInstance(type)!).Initialize(workspace);
        CreateDefaultScript(workspace);
        CreateDefaultAssemblyDefinition(workspace);
        CreateDefaultEditorScript(workspace);
        CreateIgnoreFile(workspace);
    }

    private static void CreateDefaultScene(ProjectWorkspace workspace)
    {
        using var scene = new BEngine.Scene("Main");

        var cameraObject = scene.CreateGameObject("Main Camera");
        cameraObject.AddComponent<BEngine.Camera2D>();

        var sprite = scene.CreateGameObject("Sprite");
        sprite.AddComponent<BEngine.SpriteRenderer>();

        SceneAssetSerialization.Save(scene, workspace.StartupScenePath);
    }

    private static void CreateDefaultScript(ProjectWorkspace workspace)
    {
        var path = Paths.Combine(workspace.ScriptsPath, "Rotator.cs");
        if (FileSystem.FileExists(path)) return;
        FileSystem.WriteAllText(path, """
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
        var path = Paths.Combine(workspace.EditorScriptsPath, "ProjectMenus.cs");
        if (FileSystem.FileExists(path)) return;
        FileSystem.WriteAllText(path, """
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
        var path = Paths.Combine(workspace.ScriptsPath, "Game.asmdef.yaml");
        if (FileSystem.FileExists(path)) return;
        new AssemblyDefinitionDocument().Save(path);
    }

    private static void CreateIgnoreFile(ProjectWorkspace workspace)
    {
        var path = Paths.Combine(workspace.RootPath, ".gitignore");
        if (FileSystem.FileExists(path)) return;
        FileSystem.WriteAllText(path, "Library/\nLogs/\nTemp/\n");
    }
}
