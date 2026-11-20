using EditorCapabilities = BEngine.Editor.EditorServices;
using System.Reflection;
using BEngine.Serialization;
using BEngine.Platform;

namespace BEngine.Editor;

internal static class ProjectAssetCreation
{
    private static FileUtility FileSystem => EditorCapabilities.GetRequiredService<FileUtility>();
    private static PathUtility Paths => EditorCapabilities.GetRequiredService<PathUtility>();
    internal static bool CanCreateFromMenu() => EditorAssetWritePolicy.CanWrite &&
                                                EditorBridge.Host?.ActiveProjectFolderPath is not null;

    internal static string CreateFolder(string folder)
    {
        var path = Unique(folder, "New Folder");
        var parent = Paths.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
        return AssetDatabase.CreateFolder(parent, Paths.GetFileName(path));
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
        var codec = GetDocumentCodec<ISceneDocumentCodec>();
        using var scene = new Scene("New Scene");
        codec.Save(scene, AssetDatabase.ResolveAssetPath(path));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return path;
    }

    internal static string CreatePrefab(string folder)
    {
        var path = Unique(folder, "New Prefab.prefab.yaml");
        using var temporaryScene = new Scene("Prefab Creation");
        var root = temporaryScene.CreateGameObject("New Prefab");
        _ = PrefabUtility.SaveAsPrefabAsset(root, path, out var success);
        if (!success) throw new IOException($"Could not create prefab at {path}.");
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

        var save = entry.AssetType.GetMethod("Save", BindingFlags.Instance | BindingFlags.Public,
            binder: null, types: [typeof(string)], modifiers: null);
        if (save is not null && suffix != ".asset.yaml")
        {
            try { save.Invoke(asset, [fullPath]); }
            catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
        }
        else
        {
            GetDocumentCodec<IManagedAssetDocumentCodec>().Save(asset, fullPath);
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return path;
    }

    private static T GetDocumentCodec<T>() where T : class =>
        DependencyInjection.RuntimeServiceContributions.GetDefault<T>() ??
        throw new InvalidOperationException($"No {typeof(T).Name} document codec is registered.");

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

    internal static string Unique(string folder, string fileName) => AssetDatabase.GenerateUniqueAssetPath(
        $"{folder.TrimEnd('/', '\\')}/{fileName}");

    internal static void WriteText(string path, string content)
    {
        AssetModificationProcessorDispatcher.OnWillCreateAsset(path);
        var fullPath = AssetDatabase.ResolveAssetPath(path);
        FileSystem.CreateDirectory(Paths.GetDirectoryName(fullPath)!);
        FileSystem.WriteAllText(fullPath, content);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }
}
