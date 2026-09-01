using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.Serialization;

namespace BEngine.ExampleTests.EditorKeyboardCommands;

internal sealed class KeyboardCommandFixture : IDisposable
{
    internal string RootPath { get; }
    internal ProjectWorkspace Workspace { get; }
    internal string ScenePath { get; }
    internal string SourceAssetPath { get; }
    internal string TextFocusAssetPath { get; }

    internal KeyboardCommandFixture()
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"BEngineEditorKeyboard_{Guid.NewGuid():N}");
        Workspace = ProjectWorkspaceFactory.Create(RootPath, "Editor Keyboard Commands");
        var sceneDirectory = Path.Combine(Workspace.AssetsPath, "Scenes");
        var shortcutDirectory = Path.Combine(Workspace.AssetsPath, "Shortcuts");
        Directory.CreateDirectory(sceneDirectory);
        Directory.CreateDirectory(shortcutDirectory);

        ScenePath = Path.Combine(sceneDirectory, "Keyboard.scene.yaml");
        SourceAssetPath = Path.Combine(shortcutDirectory, "Source.txt");
        TextFocusAssetPath = Path.Combine(shortcutDirectory, "TextFocus.txt");
        File.WriteAllText(SourceAssetPath, "keyboard-source");
        File.WriteAllText(TextFocusAssetPath, "text-focus-source");

        var scene = new Scene("Keyboard Scene");
        var root = scene.CreateGameObject("Keyboard Root");
        root.transform.localPosition = new Vector2(2, 3);
        var child = scene.CreateGameObject("Keyboard Child");
        child.transform.SetParent(root.transform, false);
        SceneAssetSerialization.Save(scene, ScenePath);
        scene.Dispose();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
