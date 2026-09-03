using System.Reflection;
using System.Runtime.Loader;
using BEngine.AssetBundles;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.ExampleTests.EditorHotUpdateFixture;
using BEngine.HotUpdate;
using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.ExampleTests.EditorHotUpdateDomain;

internal static class Program
{
    private static async Task<int> Main()
    {
        try
        {
            await VerifyEditorInjectionAsync().ConfigureAwait(false);
            Console.WriteLine("EDITOR_HOT_UPDATE_DOMAIN_OK|virtual-code-ab,collectible-domain,type-priority," +
                              "hot-scene-components,inspector-write,playmode-di,hot-runtime-system,type-restore");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"EDITOR_HOT_UPDATE_DOMAIN_FAILED|{exception}");
            return 1;
        }
    }

    private static async Task VerifyEditorInjectionAsync()
    {
        RuntimeTypeCache.Warmup();
        var editType = typeof(HotDomainProbeComponent);
        Require(ReferenceEquals(RuntimeTypeCache.FindType(editType.FullName!), editType),
            "The edit-time component is not the active type before Play Mode.");

        using var editScene = new Scene("Edit Scene");
        var editObject = editScene.CreateGameObject("Probe");
        var editComponent = editObject.AddComponent<HotDomainProbeComponent>();
        editComponent.Counter = 17;
        editComponent.Payload = new HotDomainProbePayload { Label = "from-edit-domain", Value = 41 };

        var assembly = typeof(HotDomainProbeComponent).Assembly;
        var moduleName = assembly.GetName().Name!;
        const string assemblyAddress = "Assets/__BEngine/HotUpdate/EditorHotUpdateDomain.dll";
        var manifest = new ManagedCodeReleaseManifest
        {
            ReleaseId = "editor-domain-test",
            Modules =
            [
                new ManagedCodeModuleManifest
                {
                    Name = moduleName,
                    BuildId = "editor-domain-test-build",
                    AssemblyAddress = assemblyAddress
                }
            ]
        };
        var snapshot = new VirtualAssetBundleSnapshot(
            "editor-hot-domain-tests",
            "editor-hot-domain-content",
            [
                VirtualAssetBundleEntry.FromMemory(
                    ManagedCodeReleaseManifest.DefaultAddress,
                    ManagedCodeReleaseManifestSerializer.Serialize(manifest),
                    "ManagedCodeReleaseManifest"),
                VirtualAssetBundleEntry.FromMemory(
                    assemblyAddress,
                    await File.ReadAllBytesAsync(assembly.Location).ConfigureAwait(false),
                    "ManagedAssembly")
            ]);
        await using var manager = new VirtualAssetBundleManager(snapshot);
        await manager.InitializeAsync().ConfigureAwait(false);
        var workspace = ProjectWorkspace.Open(Path.Combine(FindRepositoryRoot(), "Example"));

        using (var hotSession = await EditorHotUpdateSession.CreateAsync(
                   workspace, manager).ConfigureAwait(false) ??
               throw new InvalidOperationException("The Editor did not create a HotUpdate session."))
        {
            var hotAssembly = hotSession.Assemblies.Single();
            Require(!ReferenceEquals(hotAssembly, assembly) &&
                    AssemblyLoadContext.GetLoadContext(hotAssembly)?.IsCollectible == true,
                "The Editor used the edit-time assembly instead of a collectible injected domain.");
            var hotType = RuntimeTypeCache.FindType(editType.FullName!);
            Require(hotType is not null && ReferenceEquals(hotType.Assembly, hotAssembly),
                "The injected component did not win the Play Mode type mapping.");
            var serviceModuleType = hotAssembly.GetType(typeof(HotDomainProbeServiceModule).FullName!, true)!;
            Require((bool)serviceModuleType.GetProperty(nameof(HotDomainProbeServiceModule.Configured))!
                        .GetValue(null)! && !HotDomainProbeServiceModule.Configured,
                "The Play Mode service module was not configured exclusively from the injected domain.");

            var sourcePath = Path.Combine(workspace.AssetsPath, "EditorHotUpdateDomain.scene.yaml");
            var undo = Undo.CaptureAndClear();
            var dirtyState = EditorUtility.CaptureDirtyState();
            var runtimeState = EditorRuntimeStateSnapshot.Capture();
            var playSession = EditorPlayModeSession.Create(
                hotSession.Services,
                [new EditorOpenScene(editScene, sourcePath, "Assets/EditorHotUpdateDomain.scene.yaml")],
                editScene,
                sourcePath,
                null,
                null,
                null,
                editObject,
                null,
                null,
                [],
                false,
                false,
                [editComponent],
                editObject,
                undo,
                null,
                dirtyState,
                null,
                runtimeState);
            var runtimeScene = playSession.RuntimeScene;
            var runtimeComponent = runtimeScene.gameObjects.Single().components.Single(component =>
                component.GetType().FullName == editType.FullName);
            Require(ReferenceEquals(runtimeComponent.GetType().Assembly, hotAssembly),
                "The runtime Scene still contains the edit-time component type.");
            Require((int)runtimeComponent.GetType().GetField(nameof(HotDomainProbeComponent.Counter))!
                        .GetValue(runtimeComponent)! == 17,
                "The runtime component lost its serialized edit-time value.");
            var payload = runtimeComponent.GetType().GetField(nameof(HotDomainProbeComponent.Payload))!
                .GetValue(runtimeComponent)!;
            Require(ReferenceEquals(payload.GetType().Assembly, hotAssembly) &&
                    (string?)payload.GetType().GetField(nameof(HotDomainProbePayload.Label))!.GetValue(payload) ==
                    "from-edit-domain",
                "A nested project type was not remapped into the injected domain.");

            using var serialized = new SerializedObject(runtimeComponent);
            var counter = serialized.FindProperty(nameof(HotDomainProbeComponent.Counter)) ??
                          throw new InvalidOperationException("Inspector did not expose the hot component field.");
            counter.intValue = 73;
            Require(serialized.ApplyModifiedPropertiesWithoutUndo(),
                "Inspector did not report a modified hot component.");
            Require((int)runtimeComponent.GetType().GetField(nameof(HotDomainProbeComponent.Counter))!
                        .GetValue(runtimeComponent)! == 73,
                "Inspector did not write to the injected component instance.");

            var playSceneManager = hotSession.Services.GetRequiredService<IRuntimeSceneManager>();
            playSceneManager.RegisterScene(runtimeScene, setActive: true);
            Require(ReferenceEquals(playSceneManager.ActiveScene, runtimeScene) &&
                    playSceneManager.LoadedScenes.Contains(runtimeScene),
                "The runtime Scene was not registered with the HotDomain SceneManager.");
            var sceneRuntime = hotSession.Services.GetRequiredService<ISceneRuntimeFactory>().Create(runtimeScene);
            try
            {
                sceneRuntime.Start();
                Require(playSceneManager.LoadedScenes.Count(scene => ReferenceEquals(scene, runtimeScene)) == 1,
                    "SceneRuntime registered the Scene with a different manager or duplicated it.");
                var runtimeSystemType = hotAssembly.GetType(typeof(HotDomainProbeRuntimeSystem).FullName!, true)!;
                Require((bool)runtimeSystemType.GetProperty(nameof(HotDomainProbeRuntimeSystem.Started))!
                            .GetValue(null)! && !HotDomainProbeRuntimeSystem.Started,
                    "The runtime system was not started exclusively from the injected Play Mode dependency graph.");
            }
            finally
            {
                sceneRuntime.Stop();
                playSceneManager.UnregisterScene(runtimeScene);
                foreach (var scene in playSession.RuntimeScenes)
                    if (scene.isCreated) scene.Dispose();
                Undo.Restore(undo);
                EditorUtility.RestoreDirtyState(dirtyState);
                runtimeState.Restore();
            }
        }

        Require(ReferenceEquals(RuntimeTypeCache.FindType(editType.FullName!), editType),
            "The edit-time component type was not restored after Play Mode.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")))
                return directory.FullName;
        throw new DirectoryNotFoundException("BEngine repository root was not found.");
    }

}
