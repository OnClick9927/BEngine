using System.Reflection;
using BEngine;
using BEngine.Animation;
using BEngine.Editor.Documents;
using BEngine.ProjectSystem;
using BEngine.Serialization;
using BEngine.UIElements;

namespace BEngine.ExampleTests.DocumentArchitecture;

internal static class Program
{
    private static int Main()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"BEngine-DocumentArchitecture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            VerifySingleGenericBridge();
            VerifyPlainProjectData(directory);
            VerifySceneRoundTrip(directory);
            VerifyPackageRoundTrips(directory);
            VerifyUiAssetReferenceContract(directory);
            VerifyEditorDataComposition();
            VerifySourceBoundary();
            Console.WriteLine("DOCUMENT_ARCHITECTURE_OK|internal-generic,basset-only,plain-data," +
                              "scene,prefab,packages,ui-reference,runtime-write-guard,boundary");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"DOCUMENT_ARCHITECTURE_FAILED|{exception}");
            return 1;
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    private static void VerifySingleGenericBridge()
    {
        var bridges = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(static assembly =>
            {
                try { return assembly.GetTypes(); }
                catch (ReflectionTypeLoadException exception) { return exception.Types.OfType<Type>(); }
            })
            .Where(static type => type.IsGenericTypeDefinition && type.Name == "Document`1")
            .ToArray();
        Require(bridges.Length == 1, $"Expected one Document<TAsset>, found {bridges.Length}.");
        var bridge = bridges[0];
        Require(bridge.IsNotPublic && bridge.IsSealed,
            "Document<TAsset> must be internal and sealed.");
        var constraints = bridge.GetGenericArguments()[0].GetGenericParameterConstraints();
        Require(constraints.Length == 1 && constraints[0] == typeof(BAsset),
            "Document<TAsset> must constrain TAsset to BAsset.");
        Require(!typeof(BAsset).IsAssignableFrom(typeof(ProjectData)),
            "Project configuration must remain plain YAML data rather than an asset.");
    }

    private static void VerifyPlainProjectData(string directory)
    {
        var source = new ProjectData { Name = "Generic Asset Bridge Test" };
        var path = Path.Combine(directory, "Project.yaml");
        YamlUtility.Save(source, path);
        var restored = YamlUtility.Load<ProjectData>(path);
        Require(restored.Name == source.Name, "Plain project data did not round-trip through YAML.");
    }

    private static void VerifySceneRoundTrip(string directory)
    {
        using var source = new Scene("Document Scene");
        var camera = source.CreateGameObject("Main Camera");
        camera.transform.localPosition = new Vector2(1, 2);
        camera.AddComponent<Camera2D>().size = 8;

        var yaml = SceneAssetSerialization.Serialize(source);
        using var memoryScene = SceneAssetSerialization.Deserialize(yaml);
        Require(memoryScene.Find("Main Camera")?.GetComponent<Camera2D>() is { size: var size } &&
                size == (Fix64)8,
            "Scene serialization lost component state.");

        var path = Path.Combine(directory, "Scene.scene.yaml");
        SceneAssetSerialization.Save(source, path);
        using var diskScene = SceneAssetSerialization.Load(path);
        Require(diskScene.Find("Main Camera") is not null,
            "Scene disk round-trip lost its GameObject hierarchy.");

        memoryScene.MarkRuntimeOnly();
        var runtimePath = Path.Combine(directory, "Runtime.scene.yaml");
        Expect<InvalidOperationException>(() => SceneAssetSerialization.Save(memoryScene, runtimePath));
        Require(!File.Exists(runtimePath), "A runtime Scene was written to disk.");
    }

    private static void VerifyPackageRoundTrips(string directory)
    {
        var clip = new AnimationClip { name = "Idle", frameRate = 30 };
        var clipPath = Path.Combine(directory, "Idle.animation.yaml");
        clip.Save(clipPath);
        Require(AnimationClip.Load(clipPath).name == "Idle",
            "AnimationClip did not use its typed asset serialization path.");

        var treeAsset = VisualTreeAsset.Create(new Label("Document UI"));
        var uiPath = Path.Combine(directory, "Document.ui.yaml");
        treeAsset.Save(uiPath);
        Require(VisualTreeAsset.Load(uiPath).Instantiate() is Label label && label.text == "Document UI",
            "VisualTreeAsset did not use its typed asset serialization path.");
    }

    private static void VerifyUiAssetReferenceContract(string directory)
    {
        var previousDataPath = Application.dataPath;
        var assetsPath = Path.Combine(directory, "Assets");
        Directory.CreateDirectory(assetsPath);
        try
        {
            SetDataPath(assetsPath);
            var treePath = Path.Combine(assetsPath, "Document.uxml");
            VisualTreeAsset.Create(new Label("Portable UI")).Save(treePath);
            var direct = VisualTreeAsset.Load(treePath);
            Require(direct.assetPath == "Assets/Document.uxml",
                "VisualTreeAsset retained an absolute project path.");

            var stylePath = Path.Combine(assetsPath, "Document.uss");
            File.WriteAllText(stylePath, ".root { color: white; }");
            Require(StyleSheet.Load(stylePath).assetPath == "Assets/Document.uss",
                "StyleSheet retained an absolute project path.");

            using var scene = new Scene("UI Asset Reference");
            var probe = scene.CreateGameObject("Probe").AddComponent<UiAssetReferenceProbe>();
            probe.asset = direct;
            var fields = ComponentFieldSerializer.Serialize(probe);
            Require(fields[nameof(UiAssetReferenceProbe.asset)] == "Assets/Document.uxml",
                "A UI BAsset component reference serialized an absolute path.");
        }
        finally
        {
            BAsset.ClearLoadedAssets();
            SetDataPath(previousDataPath);
        }
    }

    private static void VerifyEditorDataComposition()
    {
        Require(typeof(EditorLayoutDocument).GetProperty(nameof(EditorLayoutDocument.DockRoot))?.PropertyType ==
                typeof(EditorDockNodeDocument) &&
                typeof(EditorLayoutDocument).GetProperty(nameof(EditorLayoutDocument.Windows))?.PropertyType ==
                typeof(List<EditorWindowLayoutDocument>),
            "Editor layout data no longer composes its dock tree and window records.");
        Require(!typeof(BAsset).IsAssignableFrom(typeof(EditorLayoutDocument)),
            "Editor layout configuration must not be represented as a BAsset.");
    }

    private static void VerifySourceBoundary()
    {
        var repository = FindRepository(AppContext.BaseDirectory);
        var documentsRoot = Path.Combine(repository, "src", "Core", "BEngine", "Documents");
        var files = Directory.EnumerateFiles(documentsRoot, "*.cs", SearchOption.AllDirectories)
            .Select(Path.GetFileName).ToArray();
        Require(files.SequenceEqual(["Document.cs"]),
            $"Core Documents contains legacy types: {string.Join(", ", files)}");

        var source = File.ReadAllText(Path.Combine(documentsRoot, "Document.cs"));
        Require(source.Contains("internal sealed class Document<TAsset> where TAsset : BAsset",
                StringComparison.Ordinal),
            "Core Document<TAsset> does not implement the required internal BAsset bridge.");
        string[] forbidden =
        [
            "DocumentConversionRegistry", "DocumentValidationRegistry", "DocumentObject",
            "IDocumentConverter", "ComponentTypeMigrationRegistry", "ProjectSettingsMigration",
            "LayerDocumentMigration"
        ];
        var sourceFiles = Directory.EnumerateFiles(Path.Combine(repository, "src"), "*.cs",
            SearchOption.AllDirectories);
        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var name in forbidden)
                Require(!text.Contains(name, StringComparison.Ordinal),
                    $"Legacy document architecture type '{name}' remains in {file}.");
        }
    }

    private static string FindRepository(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate BEngine repository root.");
    }

    private static void Expect<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void SetDataPath(string value) => typeof(Application).GetProperty(
        nameof(Application.dataPath))!.GetSetMethod(nonPublic: true)!.Invoke(null, [value]);
}

internal sealed class UiAssetReferenceProbe : MonoBehaviour
{
    public UiAssetReferenceProbe() { }
    public BAsset? asset { get; set; }
}
