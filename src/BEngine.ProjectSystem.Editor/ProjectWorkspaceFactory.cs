using BEngine.Serialization;
using BEngine.Serialization.Documents;
using BEngine.Serialization.Editor;
using BEngine.Serialization.Editor.Documents;

namespace BEngine.ProjectSystem.Editor;

public static class ProjectWorkspaceFactory
{
    public static ProjectWorkspace Create(string rootPath, string projectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        var root = Path.GetFullPath(rootPath);
        var projectPath = Path.Combine(root, ProjectWorkspace.ProjectFileName);
        if (File.Exists(projectPath))
            throw new IOException($"A BEngine project already exists at {root}.");

        Directory.CreateDirectory(root);
        var trimmedName = projectName.Trim();
        var project = new ProjectDocument
        {
            Name = trimmedName,
            Window = new WindowDocument { Title = trimmedName }
        };
        var workspace = new ProjectWorkspace(root, project);
        new YamlProjectSerializer().Save(project, workspace.ProjectFilePath);

        var editorSerializer = new YamlEditorSerializer();
        editorSerializer.SaveEditorSettings(new EditorSettingsDocument(),
            Path.Combine(workspace.ProjectSettingsPath, "EditorSettings.yaml"));
        editorSerializer.SaveEditorLayout(new EditorLayoutDocument(), workspace.EditorLayoutPath);
        YamlUtility.Save(new ProjectSettingsDocument
        {
            ProductName = project.Name,
            DefaultScreenWidth = project.Window.Width,
            DefaultScreenHeight = project.Window.Height
        }, workspace.ProjectSettingsFilePath);

        _ = new BPackageManager(workspace);
        CreateDefaultScene(workspace);
        CreateDefaultScript(workspace);
        CreateDefaultAssemblyDefinition(workspace);
        CreateDefaultEditorScript(workspace);
        CreateIgnoreFile(workspace);
        return workspace;
    }

    private static void CreateDefaultScene(ProjectWorkspace workspace)
    {
        var scene = new BEngine.Scene("Main");

        var cameraObject = scene.CreateGameObject("Main Camera");
        cameraObject.transform.localPosition = new BEngine.Vector3(5, BEngine.Fix64.Parse("3.5"), -7);
        cameraObject.transform.localEulerAngles = new BEngine.Vector3(18, -35, 0);
        cameraObject.AddComponent<BEngine.Camera>();

        var lightObject = scene.CreateGameObject("Directional Light");
        lightObject.transform.localEulerAngles = new BEngine.Vector3(50, -30, 0);
        lightObject.AddComponent<BEngine.DirectionalLight>();

        var pointLightObject = scene.CreateGameObject("Point Light");
        pointLightObject.transform.localPosition = new BEngine.Vector3(-2, 2, -1);
        var pointLight = pointLightObject.AddComponent<BEngine.Light>();
        pointLight.type = BEngine.LightType.Point;
        pointLight.color = new BEngine.Color(
            BEngine.Fix64.One, BEngine.Fix64.Parse("0.62"), BEngine.Fix64.Parse("0.32"));
        pointLight.intensity = 4;
        pointLight.range = 6;
        pointLight.shadows = BEngine.LightShadows.None;

        var lightingSettingsObject = scene.CreateGameObject("Lighting Settings");
        lightingSettingsObject.AddComponent<BEngine.LightingSettings>();

        var skyboxObject = scene.CreateGameObject("Skybox");
        skyboxObject.AddComponent<BEngine.Skybox>();

        var cube = scene.CreateGameObject("Cube");
        cube.transform.localPosition = new BEngine.Vector3(0, BEngine.Fix64.Half, 0);
        cube.AddComponent<BEngine.MeshRenderer>();

        var ground = scene.CreateGameObject("Ground");
        ground.transform.localScale = new BEngine.Vector3(12, 1, 12);
        var groundRenderer = ground.AddComponent<BEngine.MeshRenderer>();
        groundRenderer.mesh = "Plane";
        groundRenderer.color = new BEngine.Color(
            BEngine.Fix64.Parse("0.22"), BEngine.Fix64.Parse("0.24"), BEngine.Fix64.Parse("0.27"));

        new YamlSceneSerializer().Save(scene, workspace.StartupScenePath);
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
                public Vector3 degreesPerSecond { get; set; } = new(0, 45, 0);

                public override void Update()
                {
                    transform.localEulerAngles += degreesPerSecond * Time.deltaTime;
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
        YamlUtility.Save(new AssemblyDefinitionDocument(), path);
    }

    private static void CreateIgnoreFile(ProjectWorkspace workspace)
    {
        var path = Path.Combine(workspace.RootPath, ".gitignore");
        if (File.Exists(path)) return;
        File.WriteAllText(path, "Library/\nLogs/\nTemp/\n");
    }
}
