using BEngine;
using BEngine.Animation;
using BEngine.Documents;
using BEngine.Editor;
using BEngine.Editor.Documents;
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
            VerifyGenericDocumentObject(directory);
            VerifySceneConversion(directory);
            VerifyPackageConversions(directory);
            VerifyUiAssetReferenceContract(directory);
            VerifyDocumentComposition();
            VerifySourceBoundary();
            Console.WriteLine("DOCUMENT_ARCHITECTURE_OK|base,object,yaml,disk,scene,packages," +
                              "ui-basset-reference,composition,boundary");
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

    private static void VerifyPackageConversions(string directory)
    {
        var clip = new AnimationClip { name = "Idle", frameRate = 30 };
        var clipPath = Path.Combine(directory, "Idle.animation.yaml");
        Document.SaveBObject<AnimationClipDocument>(clip, clipPath);
        Require(Document.LoadBObject<AnimationClipDocument, AnimationClip>(clipPath).name == "Idle",
            "Animation package did not register its Document converter.");

        var treeAsset = VisualTreeAsset.Create(new Label("Document UI"));
        var uiPath = Path.Combine(directory, "Document.ui.yaml");
        Document.SaveBObject<UIAssetDocument>(treeAsset, uiPath);
        Require(Document.LoadBObject<UIAssetDocument, VisualTreeAsset>(uiPath).Instantiate() is Label label &&
                label.text == "Document UI",
            "UIElements package did not register its Document converter.");
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

            var scene = new Scene("UI Asset Reference");
            var probe = scene.CreateGameObject("Probe").AddComponent<UiAssetReferenceProbe>();
            probe.asset = direct;
            var fields = ComponentFieldSerializer.Serialize(probe);
            Require(fields[nameof(UiAssetReferenceProbe.asset)] == "Assets/Document.uxml",
                "A UI BAsset component reference serialized an absolute path.");

            var first = BAsset.Load<VisualTreeAsset>("Assets/Document.uxml");
            BAsset.ClearLoadedAssets();
            var second = BAsset.Load<VisualTreeAsset>("Assets/Document.uxml");
            Require(first is not null && second is not null && !ReferenceEquals(first, second),
                "VisualTreeAsset bypassed the unified BAsset cache invalidation contract.");
            scene.Dispose();
        }
        finally
        {
            BAsset.ClearLoadedAssets();
            SetDataPath(previousDataPath);
        }
    }

    private static void VerifyDocumentComposition()
    {
        Type[] persistentRoots =
        {
            typeof(ProjectDocument),
            typeof(SceneDocument),
            typeof(EditorLayoutDocument),
            typeof(AnimationClipDocument),
            typeof(UIAssetDocument)
        };
        var violations = persistentRoots.Where(type => !typeof(Document).IsAssignableFrom(type))
            .Select(type => type.FullName).ToArray();
        Require(violations.Length == 0,
            $"Persistent document roots do not inherit Document: {string.Join(", ", violations)}");

        Require(!typeof(Document).IsAssignableFrom(typeof(EditorDockNodeDocument)) &&
                !typeof(Document).IsAssignableFrom(typeof(EditorWindowLayoutDocument)),
            "Editor layout child records must remain composed DTOs rather than independent document roots.");
        Require(typeof(EditorLayoutDocument).GetProperty(nameof(EditorLayoutDocument.DockRoot))?.PropertyType ==
                typeof(EditorDockNodeDocument) &&
                typeof(EditorLayoutDocument).GetProperty(nameof(EditorLayoutDocument.Windows))?.PropertyType ==
                typeof(List<EditorWindowLayoutDocument>),
            "EditorLayoutDocument no longer composes the dock tree and window records.");
    }

    private static void VerifyGenericDocumentObject(string directory)
    {
        var project = new ProjectDocument { Name = "Document Test" };
        Require(project is Document, "ProjectDocument does not inherit Document.");
        var wrapper = project.ToBObject();
        Require(wrapper is DocumentObject { document: ProjectDocument restored } && restored.Name == project.Name,
            "A document without a semantic converter did not use the lossless DocumentObject bridge.");
        Require(ReferenceEquals(Document.FromBObject(wrapper), project),
            "DocumentObject did not convert back through the unified Document API.");

        var path = Path.Combine(directory, "Project.yaml");
        project.Save(path);
        var diskObject = Document.LoadBObject<ProjectDocument>(path);
        Require(diskObject is DocumentObject { document: ProjectDocument loaded } && loaded.Name == project.Name,
            "Disk Document -> BObject conversion did not preserve the concrete document.");
    }

    private static void VerifySceneConversion(string directory)
    {
        var source = new Scene("Document Scene");
        var camera = source.CreateGameObject("Main Camera");
        camera.transform.localPosition = new Vector2(1, 2);
        var cameraComponent = camera.AddComponent<Camera2D>();
        cameraComponent.size = 8;

        var document = Document.FromBObject<SceneDocument>(source);
        Require(document.GameObjects.Count == 1, "Scene -> Document conversion lost a GameObject.");
        var memoryScene = document.ToBObject();
        Require(memoryScene is Scene { name: "Document Scene" } scene &&
                scene.Find("Main Camera")?.GetComponent<Camera2D>() is { size: var size } &&
                size == (Fix64)8,
            "Document -> Scene conversion lost component state.");

        var path = Path.Combine(directory, "Scene.scene.yaml");
        Document.SaveBObject<SceneDocument>(source, path);
        var diskScene = Document.LoadBObject<SceneDocument, Scene>(path);
        Require(diskScene.Find("Main Camera") is not null,
            "Unified disk Document -> BObject conversion did not restore the Scene.");
    }

    private static void VerifySourceBoundary()
    {
        var repository = FindRepository(AppContext.BaseDirectory);
        var serializationRoot = Path.Combine(repository, "src", "Core", "BEngine", "Core", "Serialization");
        var files = Directory.EnumerateFiles(serializationRoot, "*.cs", SearchOption.AllDirectories)
            .Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        string[] expected = ["ISerializationCallbackReceiver.cs", "SerializationCallbackUtility.cs", "YamlUtility.cs"];
        Require(files.SequenceEqual(expected.OrderBy(name => name, StringComparer.Ordinal)),
            $"Core Serialization contains concrete types: {string.Join(", ", files)}");

        var forbidden = Directory.EnumerateDirectories(Path.Combine(repository, "src"), "Documents",
            SearchOption.AllDirectories).Where(path => path.Split(Path.DirectorySeparatorChar)
            .Any(segment => segment.Equals("Serialization", StringComparison.OrdinalIgnoreCase))).ToArray();
        Require(forbidden.Length == 0,
            $"Documents is still nested under Serialization: {string.Join(", ", forbidden)}");
    }

    private static string FindRepository(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate BEngine repository root.");
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
