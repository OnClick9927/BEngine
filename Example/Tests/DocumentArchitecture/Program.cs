using BEngine;
using BEngine.Animation;
using BEngine.Documents;
using BEngine.Editor;
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
            VerifyDocumentInheritance();
            VerifySourceBoundary();
            Console.WriteLine("DOCUMENT_ARCHITECTURE_OK|base,object,yaml,disk,scene,packages,inheritance,boundary");
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

    private static void VerifyDocumentInheritance()
    {
        var assemblies = new[]
        {
            typeof(Document).Assembly,
            typeof(EditorWindow).Assembly,
            typeof(AnimationClipDocument).Assembly,
            typeof(UIAssetDocument).Assembly
        };
        var violations = assemblies.Distinct().SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Name.EndsWith("Document", StringComparison.Ordinal) &&
                           type != typeof(BEngine.UIElements.UIDocument) &&
                           !typeof(Document).IsAssignableFrom(type))
            .Select(type => type.FullName).ToArray();
        Require(violations.Length == 0,
            $"Document types do not inherit the common base: {string.Join(", ", violations)}");
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
        var serializationRoot = Path.Combine(repository, "src", "Core", "BEngine", "Serialization");
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
}
