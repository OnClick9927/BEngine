using System.Reflection;
using BEngine.Animation;
using BEngine.Editor;
using BEngine.ProjectSystem;
using BEngine.Serialization;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.ExampleTests.ProjectAssetWorkflow;

internal static class UnifiedBAssetArchitectureTests
{
    internal static void Run()
    {
        VerifyUnifiedTypeHierarchy();
        VerifyInternalDocumentBridge();
        VerifyEditorCacheAndRuntimeWritePolicy();
        Console.WriteLine("UNIFIED_BASSET_ARCHITECTURE_OK|single-basset-hierarchy,internal-document-bridge," +
                          "cached-asset-queries,main-asset-cache,import-invalidation,refresh-invalidation," +
                          "runtime-read-only");
    }

    private static void VerifyUnifiedTypeHierarchy()
    {
        var assemblies = new[]
        {
            typeof(BAsset).Assembly,
            typeof(AssetDatabase).Assembly,
            typeof(AnimationClip).Assembly
        }.Distinct().ToArray();

        var forbidden = assemblies.SelectMany(GetLoadableTypes).FirstOrDefault(type =>
            type.Name is "DocumentAsset" or "FileAsset");
        Require(forbidden is null,
            $"The removed split asset type '{forbidden?.FullName}' is still present.");

        Type[] assetTypes =
        [
            typeof(BEngine.Font), typeof(BEngine.Texture), typeof(BEngine.Script), typeof(BEngine.TextAsset),
            typeof(Shader), typeof(Material), typeof(PrefabAsset), typeof(Scene), typeof(ScriptableObject),
            typeof(TextureAtlas), typeof(DefaultAsset), typeof(AssemblyDefinitionAsset), typeof(GUISkin),
            typeof(BEngine.Editor.TextAsset), typeof(MonoScript),
            typeof(AnimationClip), typeof(AnimatorController)
        ];
        Require(assetTypes.All(type => typeof(BAsset).IsAssignableFrom(type)),
            "A concrete engine asset escaped the unified BAsset hierarchy.");
        Require(typeof(Sprite).BaseType == typeof(BObject) && !typeof(BAsset).IsAssignableFrom(typeof(Sprite)),
            "Sprite must remain a Texture-created BObject instead of a standalone BAsset.");
    }

    private static void VerifyInternalDocumentBridge()
    {
        var coreAssembly = typeof(BAsset).Assembly;
        var document = GetLoadableTypes(coreAssembly).SingleOrDefault(type =>
            type.IsGenericTypeDefinition &&
            type.FullName == "BEngine.Documents.Document`1");
        Require(document is not null, "The internal Document<TAsset> persistence bridge is missing.");
        Require(document!.IsNotPublic && document.IsSealed,
            "Document<TAsset> must be internal and sealed.");

        var parameter = document.GetGenericArguments().Single();
        var constraints = parameter.GetGenericParameterConstraints();
        Require(constraints.Length == 1 && constraints[0] == typeof(BAsset),
            "Document<TAsset> must be constrained directly to the unified BAsset base type.");
    }

    private static void VerifyEditorCacheAndRuntimeWritePolicy()
    {
        var root = Path.Combine(Path.GetTempPath(), "BEngineUnifiedBAsset", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        TestEditorHost? host = null;
        Scene? first = null;
        Scene? imported = null;
        Scene? refreshed = null;
        try
        {
            new ProjectData { Name = "Unified BAsset Test" }.Save(Path.Combine(root, "Project.yaml"));
            var workspace = ProjectWorkspace.Open(root);
            var sourcePath = Path.Combine(workspace.AssetsPath, "Cache.scene.yaml");
            WriteScene(sourcePath, "Initial");

            var projectAssets = new ProjectAssetDatabase(workspace);
            projectAssets.Refresh();
            VerifyCachedAssetQueries(projectAssets);
            host = new TestEditorHost(workspace, projectAssets);
            EditorBridge.Attach(host);

            first = AssetDatabase.LoadAssetAtPath<Scene>("Assets/Cache.scene.yaml");
            var repeated = AssetDatabase.LoadAssetAtPath<Scene>("Assets/Cache.scene.yaml");
            Require(first is { name: "Initial" } && ReferenceEquals(first, repeated),
                "Repeated main-asset loads did not return the same cached BAsset instance.");

            WriteScene(sourcePath, "Imported");
            AssetDatabase.ImportAsset("Assets/Cache.scene.yaml", ImportAssetOptions.ForceUpdate);
            imported = AssetDatabase.LoadAssetAtPath<Scene>("Assets/Cache.scene.yaml");
            Require(imported is { name: "Imported" } && !ReferenceEquals(first, imported),
                "ImportAsset did not invalidate the cached main BAsset.");
            Require(ReferenceEquals(imported,
                    AssetDatabase.LoadAssetAtPath<Scene>("Assets/Cache.scene.yaml")),
                "The main BAsset was not cached again after ImportAsset.");

            WriteScene(sourcePath, "Refreshed");
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            refreshed = AssetDatabase.LoadAssetAtPath<Scene>("Assets/Cache.scene.yaml");
            Require(refreshed is { name: "Refreshed" } && !ReferenceEquals(imported, refreshed),
                "Refresh did not invalidate the cached main BAsset.");

            var sourceBeforePlay = File.ReadAllBytes(sourcePath);
            host.IsPlaying = true;
            var runtimeAsset = refreshed ?? throw new InvalidOperationException("The refreshed Scene was null.");
            runtimeAsset.name = "Runtime Mutation";
            RequireThrows<InvalidOperationException>(() => AssetDatabase.SaveAsset(runtimeAsset),
                "SaveAsset accepted a runtime mutation.");
            RequireThrows<InvalidOperationException>(() =>
                    AssetDatabase.ImportAsset("Assets/Cache.scene.yaml", ImportAssetOptions.ForceUpdate),
                "ImportAsset wrote project state while Play Mode was active.");
            RequireThrows<InvalidOperationException>(() => AssetDatabase.Refresh(),
                "Refresh wrote project state while Play Mode was active.");
            Require(File.ReadAllBytes(sourcePath).SequenceEqual(sourceBeforePlay),
                "A rejected Play Mode asset operation still modified the source file.");
        }
        finally
        {
            if (host is not null)
            {
                host.IsPlaying = false;
                EditorBridge.Detach(host);
                host.Dispose();
            }
            first?.Dispose();
            if (!ReferenceEquals(imported, first)) imported?.Dispose();
            if (!ReferenceEquals(refreshed, imported) && !ReferenceEquals(refreshed, first)) refreshed?.Dispose();
            BAsset.ClearLoadedAssets();
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void VerifyCachedAssetQueries(ProjectAssetDatabase assets)
    {
        var snapshot = assets.FindAssets(string.Empty);
        _ = assets.FindAssets(string.Empty);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 512; index++)
            Require(ReferenceEquals(snapshot, assets.FindAssets(string.Empty)),
                "An unchanged empty asset query did not reuse the ordered database snapshot.");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Require(allocated == 0,
            $"Cached empty asset queries allocated {allocated} bytes.");

        var filtered = assets.FindAssets("Cache");
        Require(filtered.Count == 1 && filtered[0].AssetPath.EndsWith("Cache.scene.yaml",
                    StringComparison.OrdinalIgnoreCase),
            "The allocation-free empty-query path changed filtered asset lookup behavior.");
    }

    private static void WriteScene(string path, string name)
    {
        using var scene = new Scene(name);
        SceneAssetSerialization.Save(scene, path);
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception) { return exception.Types.OfType<Type>(); }
    }

    private static void RequireThrows<TException>(Action action, string message) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
