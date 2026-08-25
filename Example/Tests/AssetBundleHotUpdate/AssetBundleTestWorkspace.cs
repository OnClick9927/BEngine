using BEngine.Editor;
using BEngine.Documents;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.ExampleTests.AssetBundleHotUpdate;

internal sealed class AssetBundleTestWorkspace : IDisposable
{
    internal const string PackageName = "com.bengine.tests.hotupdate";
    internal const string SharedAddress = "Assets/Data/shared.txt";
    internal const string BinaryAddress = "Assets/Data/payload.bin";
    internal const string ResourceAddress = "Assets/Resources/config.txt";
    internal const string SceneAddress = "Assets/Scenes/main.scene.yaml";

    private readonly string _sharedPath;

    internal AssetBundleTestWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), $"BEngineAssetBundleHotUpdate_{Guid.NewGuid():N}");
        Workspace = ProjectWorkspaceFactory.Create(Path.Combine(Root, "Project"), "Asset Bundle Hot Update");
        var dataDirectory = Path.Combine(Workspace.AssetsPath, "Data");
        var sceneDirectory = Path.Combine(Workspace.AssetsPath, "Scenes");
        var resourcesDirectory = Path.Combine(Workspace.AssetsPath, "Resources");
        var editorDirectory = Path.Combine(Workspace.AssetsPath, "Editor");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(sceneDirectory);
        Directory.CreateDirectory(resourcesDirectory);
        Directory.CreateDirectory(editorDirectory);
        _sharedPath = Path.Combine(dataDirectory, "shared.txt");
        File.WriteAllText(_sharedPath, "shared-v1");
        File.WriteAllBytes(Path.Combine(dataDirectory, "payload.bin"), [0, 1, 2, 3, 4, 255]);
        File.WriteAllText(Path.Combine(resourcesDirectory, "config.txt"), "resource-v1");
        WriteScene("Bundled Scene", "Bundled Root");
        File.WriteAllText(Path.Combine(editorDirectory, "ignored.txt"), "editor-only");
        AssetDatabase = new ProjectAssetDatabase(Workspace);
        _ = AssetDatabase.Refresh();
    }

    internal string Root { get; }
    internal ProjectWorkspace Workspace { get; }
    internal ProjectAssetDatabase AssetDatabase { get; }

    internal AssetBundleBuildResult Build(
        string version,
        string sharedText,
        string outputName,
        bool reverseInput = false)
    {
        File.WriteAllText(_sharedPath, sharedText);
        File.SetLastWriteTimeUtc(_sharedPath,
            reverseInput ? new DateTime(2031, 5, 6, 7, 8, 10, DateTimeKind.Utc) :
                new DateTime(2021, 1, 2, 3, 4, 6, DateTimeKind.Utc));
        _ = AssetDatabase.Refresh();

        AssetBundleBuildDefinition shared = new()
        {
            Name = "shared",
            AssetPaths = reverseInput
                ? ["Assets/Editor/ignored.txt", ResourceAddress, BinaryAddress, SharedAddress]
                : ["Assets/Data", "Assets/Resources", "Assets/Editor"]
        };
        AssetBundleBuildDefinition main = new()
        {
            Name = "main",
            AssetPaths = [SceneAddress],
            Dependencies = ["shared"]
        };
        var definitions = reverseInput
            ? new[] { main, shared }
            : new[] { shared, main };
        return AssetBundleBuilder.Build(
            Workspace,
            AssetDatabase,
            definitions,
            new AssetBundleBuildOptions
            {
                PackageName = PackageName,
                Version = version,
                OutputDirectory = Path.Combine(Root, "Builds", outputName)
            });
    }

    internal void WriteScene(string sceneName, string rootName)
    {
        var path = Path.Combine(Workspace.AssetsPath, "Scenes", "main.scene.yaml");
        var scene = new Scene(sceneName);
        scene.CreateGameObject(rootName);
        Document.SaveBObject<SceneDocument>(scene, path);
        scene.Dispose();
    }

    public void Dispose()
    {
        if (Environment.GetEnvironmentVariable("BENGINE_KEEP_TEST_TEMP") == "1")
        {
            Console.WriteLine($"ASSET_BUNDLE_HOT_UPDATE_TEMP|{Root}");
            return;
        }
        if (!Directory.Exists(Root)) return;
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
