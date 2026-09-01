using System.Reflection;
using BEngine.Documents;
using BEngine.Serialization;

namespace BEngine.Editor;

internal static class ProjectAssetCreation
{
    [MenuItem("Assets/Create/Folder", false, 10)]
    private static void CreateFolderFromMenu() => CreateAndReveal(CreateFolder);

    [MenuItem("Assets/Create/Folder", true)]
    [MenuItem("Assets/Create/C# Script", true)]
    [MenuItem("Assets/Create/Scripting/ScriptableObject Script", true)]
    [MenuItem("Assets/Create/Scene", true)]
    [MenuItem("Assets/Create/Prefab", true)]
    [MenuItem("Assets/Create/Shader", true)]
    [MenuItem("Assets/Create/Text/Text File", true)]
    [MenuItem("Assets/Create/Text/Markdown File", true)]
    [MenuItem("Assets/Create/Data/JSON File", true)]
    [MenuItem("Assets/Create/Data/YAML File", true)]
    private static bool ValidateCreateFromMenu() => CanCreateFromMenu();

    [MenuItem("Assets/Create/C# Script", false, 20)]
    private static void CreateScriptFromMenu() => CreateAndReveal(CreateScript);

    [MenuItem("Assets/Create/Scripting/ScriptableObject Script", false, 21)]
    private static void CreateScriptableObjectFromMenu() => CreateAndReveal(CreateScriptableObjectScript);

    [MenuItem("Assets/Create/Scene", false, 30)]
    private static void CreateSceneFromMenu() => CreateAndReveal(CreateScene);

    [MenuItem("Assets/Create/Prefab", false, 40)]
    private static void CreatePrefabFromMenu() => CreateAndReveal(CreatePrefab);

    [MenuItem("Assets/Create/Shader", false, 200)]
    private static void CreateShaderFromMenu() => CreateAndReveal(CreateShader);

    [MenuItem("Assets/Create/Text/Text File", false, 400)]
    private static void CreateTextFromMenu() => CreateAndReveal(CreateText);

    [MenuItem("Assets/Create/Text/Markdown File", false, 401)]
    private static void CreateMarkdownFromMenu() => CreateAndReveal(CreateMarkdown);

    [MenuItem("Assets/Create/Data/JSON File", false, 410)]
    private static void CreateJsonFromMenu() => CreateAndReveal(CreateJson);

    [MenuItem("Assets/Create/Data/YAML File", false, 411)]
    private static void CreateYamlFromMenu() => CreateAndReveal(CreateYaml);

    internal static bool CanCreateFromMenu() => EditorAssetWritePolicy.CanWrite &&
                                                EditorBridge.Host?.ActiveProjectFolderPath is not null;

    internal static string CreateFolder(string folder)
    {
        var path = Unique(folder, "New Folder");
        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
        return AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    internal static string CreateScript(string folder)
    {
        var path = Unique(folder, "NewBehaviour.cs");
        WriteText(path, "using BEngine;\n\npublic sealed class NewBehaviour : MonoBehaviour\n{\n}\n");
        EditorApplication.delayCall += () => CompilationPipeline.RequestScriptCompilation();
        return path;
    }

    internal static string CreateScriptableObjectScript(string folder)
    {
        var path = Unique(folder, "NewScriptableObject.cs");
        WriteText(path, "using BEngine;\n\npublic sealed class NewScriptableObject : ScriptableObject\n{\n}\n");
        EditorApplication.delayCall += () => CompilationPipeline.RequestScriptCompilation();
        return path;
    }

    internal static string CreateScene(string folder)
    {
        var path = Unique(folder, "New Scene.scene.yaml");
        AssetModificationProcessorDispatcher.OnWillCreateAsset(path);
        SceneAssetSerialization.Save(new Scene("New Scene"), AssetDatabase.ResolveAssetPath(path));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return path;
    }

    internal static string CreatePrefab(string folder)
    {
        var path = Unique(folder, "New Prefab.prefab.yaml");
        var temporaryScene = new Scene("Prefab Creation");
        var root = temporaryScene.CreateGameObject("New Prefab");
        _ = PrefabUtility.SaveAsPrefabAsset(root, path, out var success);
        if (!success) throw new IOException($"Could not create prefab at {path}.");
        return path;
    }

    internal static string CreateShader(string folder)
    {
        var path = Unique(folder, "New Shader.shader");
        WriteText(path, """
            #pragma stage fragment
            #version 450

            layout(location = 0) out vec4 OutputColor;

            void main()
            {
                OutputColor = vec4(1.0, 1.0, 1.0, 1.0);
            }
            """);
        return path;
    }

    internal static string CreateText(string folder)
    {
        var path = Unique(folder, "New Text File.txt");
        WriteText(path, string.Empty);
        return path;
    }

    internal static string CreateMarkdown(string folder)
    {
        var path = Unique(folder, "New Markdown.md");
        WriteText(path, "# New Markdown\n");
        return path;
    }

    internal static string CreateJson(string folder)
    {
        var path = Unique(folder, "New Data.json");
        WriteText(path, "{}\n");
        return path;
    }

    internal static string CreateYaml(string folder)
    {
        var path = Unique(folder, "New Data.yaml");
        WriteText(path, "---\nname: New Data\n");
        return path;
    }

    internal static string CreateAttributed(string folder, CreateAssetMenuEntry entry)
    {
        if (!RuntimeTypeCache.TryCreateInstance(entry.AssetType, out var created) || created is not BObject asset)
            throw new InvalidOperationException($"Could not create asset type {entry.AssetType.FullName}.");
        var suffix = AssetTypeRegistry.FindFileSuffix(entry.AssetType.Name) ?? ".asset.yaml";
        var fileName = AssetPathUtility.SplitNameAndExtension(entry.FileName).Name;
        var path = Unique(folder, fileName + suffix);
        var fullPath = AssetDatabase.ResolveAssetPath(path);
        AssetModificationProcessorDispatcher.OnWillCreateAsset(path);

        var save = entry.AssetType.GetMethod(nameof(Material.Save), BindingFlags.Instance | BindingFlags.Public,
            binder: null, types: [typeof(string)], modifiers: null);
        if (save is not null && suffix != ".asset.yaml")
        {
            try { save.Invoke(asset, [fullPath]); }
            catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
        }
        else
        {
            new ManagedAssetData
            {
                TypeName = entry.AssetType.AssemblyQualifiedName ?? entry.AssetType.FullName ?? entry.AssetType.Name,
                Data = YamlUtility.Serialize(asset)
            }.Save(fullPath);
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return path;
    }

    internal static void CreateAttributedFromMenu(CreateAssetMenuEntry entry) =>
        CreateAndReveal(folder => CreateAttributed(folder, entry));

    internal static void CreateAndReveal(Func<string, string> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        var host = EditorBridge.Host;
        var folder = host?.ActiveProjectFolderPath;
        if (host is null || folder is null) return;
        try
        {
            var path = create(folder);
            if (!string.IsNullOrWhiteSpace(path)) host.RevealProjectAsset(path, beginRename: true);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static string Unique(string folder, string fileName) => AssetDatabase.GenerateUniqueAssetPath(
        $"{folder.TrimEnd('/', '\\')}/{fileName}");

    private static void WriteText(string path, string content)
    {
        AssetModificationProcessorDispatcher.OnWillCreateAsset(path);
        var fullPath = AssetDatabase.ResolveAssetPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content, new System.Text.UTF8Encoding(false));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }
}
