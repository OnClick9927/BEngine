using EditorCapabilities = BEngine.Editor.EditorServices;
using System.Runtime.CompilerServices;
using BEngine.Serialization;
using BEngine.Platform;

namespace BEngine.Editor;

internal static class BuiltInAssetTypeRegistration
{
    private static FileUtility FileSystem => EditorCapabilities.GetRequiredService<FileUtility>();
    private static PathUtility Paths => EditorCapabilities.GetRequiredService<PathUtility>();
    [ModuleInitializer]
    internal static void Register()
    {
        AssetTypeRegistry.Register<MonoScript>(".cs", "Script", LoadScript,
            importerType: typeof(ScriptImporter));
        AssetTypeRegistry.Register<Scene>(".scene.yaml", "Scene", LoadScene);
        AssetTypeRegistry.Register<PrefabAsset>(".prefab.yaml", "Prefab");
        EditorAssetAdapters.Register(new ScriptAssetInspector());
        EditorAssetAdapters.Register(new DefaultAssetPreview());
    }

    private sealed class ScriptAssetInspector : EditorAssetAdapter, IEditorAssetInspector
    {
        public override Type AssetType => typeof(Script);
        public override bool ReadOnly => false;
        public override IEditorAssetInspector Inspector => this;
        public void DrawInformation(BAsset asset, IEditorInspectorGUI gui) =>
            gui.ReadOnlyText("Script Class", ((Script)asset).GetClass()?.FullName ?? "None");
    }

    private static MonoScript LoadScript(AssetLoadContext context)
    {
        var script = new MonoScript { name = Paths.GetFileName(context.SourcePath) };
        script.SetImportedContents(FileSystem.ReadAllText(context.ImportedPath),
            FindScriptClass(Paths.GetFileNameWithoutExtension(context.SourcePath)));
        return script;
    }

    private static Scene LoadScene(AssetLoadContext context)
    {
        var codec = DependencyInjection.RuntimeServiceContributions.GetDefault<ISceneDocumentCodec>() ??
            throw new InvalidOperationException("No scene document codec is registered.");
        var scene = codec.Load(context.ImportedPath, null);
        RuntimeAssetReferences.BindScenePath(scene, context.AssetPath);
        return scene;
    }

    private static Type? FindScriptClass(string typeName) => RuntimeTypeCache.GetAllTypes()
        .FirstOrDefault(type => type.Name.Equals(typeName, StringComparison.Ordinal) &&
                                (typeof(MonoBehaviour).IsAssignableFrom(type) ||
                                 typeof(ScriptableObject).IsAssignableFrom(type)));

}
