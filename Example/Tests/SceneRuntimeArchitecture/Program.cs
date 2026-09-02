using BEngine.AssetBundles;
using BEngine.SceneManagement;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.ExampleTests.SceneRuntimeArchitecture;

internal static class Program
{
    private static int Main()
    {
        try
        {
            VerifyRemovedInfrastructure();
            CoreRuntimeSafetyTests.Run();
            AsyncOperationTests.Run();
            InputTests.Run();
            AudioTests.Run();
            ProfilerTests.Run();
            TwoDimensionalRenderingTests.Run();
            VerifyManagedSceneQueries();
            VerifyRuntimeLifecycle();
            VerifyRuntimeObjectDomainIsolation();
            VerifyFailedRuntimeStartRollsBack();
            VerifyFailedSystemCreationDisposesCreatedScopes();
            VerifyReentrantSingleSceneLoadDuringAwake();
            VerifySceneSerializationRoundtrip();
            VerifyObjectGraphSerializationRoundtrip();
            RuntimeHotPathTests.Run();
            Console.WriteLine(
                "SCENE_RUNTIME_ARCHITECTURE_OK|no-ecs,no-runtime-threading,managed-query,destroy," +
                "single-thread-systems,monobehaviour,async-operation,input-devices,audio-2d,runtime-profiler,rendering-2d,current-scene,object-domain,start-rollback," +
                "failed-scope-cleanup,reentrant-single,yaml-roundtrip,object-graph,core-runtime-safety");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"SCENE_RUNTIME_ARCHITECTURE_FAILED|{exception}");
            return 1;
        }
    }

    private static void VerifyRemovedInfrastructure()
    {
        var assembly = typeof(Scene).Assembly;
        Require(assembly.GetType("BEngine.Entities.World") is null,
            "The removed ECS World type is still exported by BEngine.");
        Require(assembly.GetType("BEngine.EngineThreadContext") is null,
            "The removed runtime thread context is still exported by BEngine.");
        Require(typeof(GameObject).GetProperty("entity") is null && typeof(Scene).GetProperty("world") is null,
            "Scene or GameObject still exposes the removed ECS facade.");
        Require(typeof(AssetBundleRuntimeOptions).GetProperty("MaxConcurrentDownloads") is null,
            "AssetBundle runtime options still expose the removed concurrent-download setting.");
    }

    private static void VerifyManagedSceneQueries()
    {
        using var scene = new Scene("Managed query test");
        var gameObject = scene.CreateGameObject("Managed");
        var behaviour = gameObject.AddComponent<SceneProbeBehaviour>();

        Require(ReferenceEquals(gameObject.GetComponent<SceneProbeBehaviour>(), behaviour),
            "GameObject.GetComponent did not read the managed component collection.");
        Require(scene.QueryComponents<SceneProbeBehaviour>() is [var found] && ReferenceEquals(found, behaviour),
            "Scene.QueryComponents did not expose a newly-added component.");

        Require(gameObject.RemoveComponent(behaviour),
            "GameObject.RemoveComponent rejected its own managed component.");
        Require(scene.QueryComponents<SceneProbeBehaviour>().Count == 0,
            "Removing a component left a stale Scene query result.");

        var replacement = gameObject.AddComponent<SceneProbeBehaviour>();
        Require(scene.Destroy(gameObject) && scene.Find("Managed") is null && gameObject.scene is null,
            "Scene destruction did not remove and unbind the GameObject.");
        Require(replacement.DestroyCount == 1,
            "Destroying a GameObject did not invoke MonoBehaviour.OnDestroy.");
    }

    private static void VerifyRuntimeLifecycle()
    {
        using var scene = new Scene("Runtime lifecycle test");
        var behaviour = scene.CreateGameObject("Behaviour").AddComponent<SceneProbeBehaviour>();
        var system = new SceneProbeSystem();
        const string registration = "scene-runtime-architecture";
        RuntimeSystemRegistry.Register(registration, () => system);
        try
        {
            var runtime = new SceneRuntime(scene);
            runtime.Start();
            runtime.Tick(Fix64.Parse("0.02"));
            runtime.Stop();

            Require(system.Calls.SequenceEqual(["Start", "FixedUpdate", "Update", "Stop"]),
                $"Runtime system order was wrong: {string.Join(", ", system.Calls)}");
            Require(system.AllCallbacksUsedSceneContext,
                "A runtime-system callback did not expose SceneRuntime.currentScene.");
            Require(behaviour.StartCount == 1 && behaviour.UpdateCount == 1,
                "SceneRuntime did not drive MonoBehaviour lifecycle exactly once.");
            Require(SceneRuntime.currentScene is null,
                "SceneRuntime leaked its current Scene after the serial callback sequence.");
        }
        finally { RuntimeSystemRegistry.Unregister(registration); }
    }

    private static void VerifyRuntimeObjectDomainIsolation()
    {
        RuntimeTypeCache.Warmup();
        var services = new RuntimeDomainServices();
        using var editorScene = new Scene("Editor original", services);
        var editorObject = editorScene.CreateGameObject("Mirrored object");
        var editorBehaviour = editorObject.AddComponent<SceneProbeBehaviour>();
        using var runtimeScene = SceneAssetSerialization.Clone(editorScene, services);
        var runtimeObject = runtimeScene.Find("Mirrored object") ??
                            throw new InvalidOperationException("The runtime mirror lost its GameObject.");
        var runtimeBehaviour = runtimeObject.GetComponent<SceneProbeBehaviour>() ??
                               throw new InvalidOperationException("The runtime mirror lost its Component.");
        using var additiveScene = new Scene("Runtime additive", services);
        var additiveObject = additiveScene.CreateGameObject("Additive object");
        var additiveBehaviour = additiveObject.AddComponent<SceneProbeBehaviour>();
        var asset = new ObjectDomainProbeAsset();

        Require(editorScene.Id == runtimeScene.Id && editorObject.Id == runtimeObject.Id &&
                editorBehaviour.Id == runtimeBehaviour.Id,
            "The object-domain test did not create same-Guid editor and runtime graphs.");
        Require(editorObject.GetInstanceID() != runtimeObject.GetInstanceID() &&
                ReferenceEquals(BObject.FindObjectFromInstanceID(editorObject.GetInstanceID()), editorObject) &&
                ReferenceEquals(BObject.FindObjectFromInstanceID(runtimeObject.GetInstanceID()), runtimeObject),
            "Same-Guid editor and runtime objects did not retain distinct instance identities.");
        Require(BObject.FindObjectsByType<SceneProbeBehaviour>().Contains(editorBehaviour) &&
                BObject.FindObjectsByType<SceneProbeBehaviour>().Contains(runtimeBehaviour),
            "Object queries changed outside a SceneRuntime context.");

        services.SceneManager.RegisterScene(additiveScene);
        ObjectDomainObservation? observation = null;
        var probe = new SceneProbeSystem
        {
            StartProbe = _ => observation = new ObjectDomainObservation(
                BObject.FindObjectsByType<Scene>(),
                BObject.FindObjectsByType<GameObject>(),
                BObject.FindObjectsByType<SceneProbeBehaviour>(),
                GameObject.Find("Mirrored object"),
                BObject.FindObjectsByType<ObjectDomainProbeAsset>())
        };
        const string registration = "scene-runtime-object-domain";
        RuntimeSystemRegistry.Register(registration, () => probe);
        var runtime = new SceneRuntime(runtimeScene);
        try
        {
            runtime.Start();
            var captured = observation ??
                           throw new InvalidOperationException("The runtime object-domain probe did not execute.");
            Require(captured.Scenes.Contains(runtimeScene) && captured.Scenes.Contains(additiveScene) &&
                    !captured.Scenes.Contains(editorScene),
                "Runtime Scene queries escaped the runtime manager's loaded Scenes.");
            Require(captured.GameObjects.Contains(runtimeObject) && captured.GameObjects.Contains(additiveObject) &&
                    !captured.GameObjects.Contains(editorObject),
                "Runtime GameObject queries exposed the retained editor graph.");
            Require(captured.Behaviours.Contains(runtimeBehaviour) &&
                    captured.Behaviours.Contains(additiveBehaviour) &&
                    !captured.Behaviours.Contains(editorBehaviour),
                "Runtime Component queries exposed the retained editor graph.");
            Require(ReferenceEquals(captured.NamedObject, runtimeObject),
                "GameObject.Find resolved the retained editor object instead of the runtime mirror.");
            Require(captured.Assets.Contains(asset),
                "Runtime object-domain filtering hid a non-Scene asset.");
        }
        finally
        {
            runtime.Stop();
            RuntimeSystemRegistry.Unregister(registration);
            services.SceneManager.UnregisterScene(runtimeScene);
            services.SceneManager.UnregisterScene(additiveScene);
        }

        Require(BObject.FindObjectsByType<SceneProbeBehaviour>().Contains(editorBehaviour) &&
                BObject.FindObjectsByType<SceneProbeBehaviour>().Contains(runtimeBehaviour),
            "Stopping SceneRuntime left object queries scoped to the former runtime domain.");
    }

    private static void VerifyFailedRuntimeStartRollsBack()
    {
        var services = new RuntimeDomainServices();
        using var scene = new Scene("Failed runtime start", services);
        const string registration = "scene-runtime-start-failure";
        RuntimeSystemRegistry.Register(registration, static () =>
            throw new InvalidOperationException("Expected runtime-system factory failure."));
        var failedRuntime = new SceneRuntime(scene);
        try
        {
            try
            {
                failedRuntime.Start();
                throw new InvalidOperationException("SceneRuntime.Start accepted a failing system factory.");
            }
            catch (InvalidOperationException exception) when
                (exception.Message == "Expected runtime-system factory failure.") { }

            Require(!failedRuntime.IsRunning && SceneRuntime.currentScene is null &&
                    !services.SceneManager.LoadedScenes.Contains(scene),
                "A failed SceneRuntime.Start left runtime state or Scene registration behind.");
        }
        finally { RuntimeSystemRegistry.Unregister(registration); }

        var recoveredRuntime = new SceneRuntime(scene);
        recoveredRuntime.Start();
        recoveredRuntime.Stop();
        services.SceneManager.UnregisterScene(scene);
    }

    private static void VerifyFailedSystemCreationDisposesCreatedScopes()
    {
        const string scopedRegistration = "a-scene-runtime-scoped-cleanup";
        const string failingRegistration = "z-scene-runtime-factory-failure";
        var scopeFactory = new TrackingScopeFactory();
        RuntimeSystemRegistry.RegisterScoped(
            scopedRegistration, scopeFactory, typeof(ScopedRuntimeProbeSystem));
        RuntimeSystemRegistry.Register(failingRegistration, static () =>
            throw new InvalidOperationException("Expected later runtime-system factory failure."));
        using var scene = new Scene("Partial system creation failure");
        try
        {
            try
            {
                new SceneRuntime(scene).Start();
                throw new InvalidOperationException("SceneRuntime accepted a partially failing system set.");
            }
            catch (InvalidOperationException exception) when
                (exception.Message == "Expected later runtime-system factory failure.") { }

            Require(scopeFactory.CreateCount == 1 && scopeFactory.DisposeCount == 1,
                "A scoped runtime system created before a later factory failure leaked its IServiceScope.");
        }
        finally
        {
            RuntimeSystemRegistry.Unregister(failingRegistration);
            RuntimeSystemRegistry.Unregister(scopedRegistration);
        }
    }

    private static void VerifyReentrantSingleSceneLoadDuringAwake()
    {
        Scene? replacement = null;
        var services = new RuntimeDomainServices((_, sceneServices) =>
            replacement = new Scene("Replacement Scene", sceneServices));
        using var scene = new Scene("Reentrant Scene", services);
        var behaviour = scene.CreateGameObject("Reentrant loader").AddComponent<SceneProbeBehaviour>();
        behaviour.AwakeProbe = () => services.SceneManager.LoadScene("Replacement.scene.yaml", LoadSceneMode.Single);
        services.SceneManager.RegisterScene(scene, setActive: true);

        var runtime = new SceneRuntime(scene);
        runtime.Start();

        TestReentrantResult();
        if (replacement is { } loaded)
            services.SceneManager.UnregisterScene(loaded, disposeScene: true);
        return;

        void TestReentrantResult()
        {
            Require(!runtime.IsRunning && !scene.isCreated,
                "A SceneRuntime continued running after its Scene was replaced during Awake.");
            Require(behaviour.StartCount == 0 && behaviour.DestroyCount == 1,
                "A component replaced during Awake continued into Start or missed OnDestroy.");
            Require(replacement is not null &&
                    services.SceneManager.LoadedScenes is [var loaded] &&
                    ReferenceEquals(loaded, replacement) &&
                    ReferenceEquals(services.SceneManager.ActiveScene, replacement),
                "A reentrant Single load did not leave only the replacement Scene active.");
            Require(SceneRuntime.currentScene is null,
                "A reentrant Single load leaked the previous runtime callback context.");
        }
    }

    private static void VerifySceneSerializationRoundtrip()
    {
        RuntimeTypeCache.Warmup();
        using var source = new Scene("Managed YAML roundtrip");
        var gameObject = source.CreateGameObject("Serialized object");
        gameObject.transform.localPosition = new Vector2(1, 2);
        gameObject.AddComponent<SceneProbeBehaviour>().Value = 42;

        var directory = Path.Combine(Path.GetTempPath(), $"BEngineSceneRuntime_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Managed.scene.yaml");
            SceneAssetSerialization.Save(source, path);
            using var restored = SceneAssetSerialization.Load(path);
            var restoredObject = restored.Find("Serialized object") ??
                                 throw new InvalidOperationException("YAML lost the GameObject.");

            Require(restoredObject.GetComponent<SceneProbeBehaviour>()?.Value == 42 &&
                    restoredObject.transform.localPosition == new Vector2(1, 2),
                "YAML roundtrip lost managed Component or Transform state.");
            Require(restored.QueryComponents<SceneProbeBehaviour>().Count == 1,
                "YAML roundtrip did not rebuild the managed Scene component collection.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void VerifyObjectGraphSerializationRoundtrip()
    {
        RuntimeTypeCache.Warmup();
        using var source = new Scene("Object graph roundtrip");
        var owner = source.CreateGameObject("Graph owner");
        var target = source.CreateGameObject("Graph target");
        var targetComponent = target.AddComponent<SceneProbeBehaviour>();
        var shared = new ObjectGraphProbeBehaviour.DerivedProbeNode
        {
            Name = "Shared",
            Weight = 27
        };
        shared.Next = shared;
        var probe = owner.AddComponent<ObjectGraphProbeBehaviour>();
        probe.SetPrivateValue(19);
        probe.Polymorphic = shared;
        probe.Values = [3, 5, 8];
        probe.Grid = new[,] { { 1, 2 }, { 3, 4 } };
        probe.Nodes = [shared];
        probe.Lookup = new Dictionary<string, ObjectGraphProbeBehaviour.ProbeNode>
        {
            ["shared"] = shared
        };
        probe.SharedA = shared;
        probe.SharedB = shared;
        probe.TargetObject = target;
        probe.TargetComponent = targetComponent;
        probe.OptionalValue = 31;
        probe.Mode = ObjectGraphProbeBehaviour.ProbeMode.Negative;

        using var restored = SceneAssetSerialization.Deserialize(SceneAssetSerialization.Serialize(source));
        var restoredOwner = restored.Find("Graph owner") ??
                            throw new InvalidOperationException("Object graph lost its owner.");
        var restoredTarget = restored.Find("Graph target") ??
                             throw new InvalidOperationException("Object graph lost its target.");
        var restoredProbe = restoredOwner.GetComponent<ObjectGraphProbeBehaviour>() ??
                            throw new InvalidOperationException("Object graph lost its Component.");
        var restoredShared = restoredProbe.SharedA as ObjectGraphProbeBehaviour.DerivedProbeNode;

        Require(restoredProbe.PrivateValue == 19 && restoredProbe.Values.SequenceEqual([3, 5, 8]) &&
                restoredProbe.Grid.GetLength(0) == 2 && restoredProbe.Grid.GetLength(1) == 2 &&
                restoredProbe.Grid[1, 1] == 4 && restoredProbe.OptionalValue == 31 &&
                restoredProbe.Mode == ObjectGraphProbeBehaviour.ProbeMode.Negative,
            "Object graph roundtrip lost scalar, private, nullable, enum, array, or multidimensional array data.");
        Require(restoredShared is { Weight: 27 } && ReferenceEquals(restoredShared.Next, restoredShared) &&
                ReferenceEquals(restoredProbe.Polymorphic, restoredShared) &&
                ReferenceEquals(restoredProbe.SharedB, restoredShared) &&
                ReferenceEquals(restoredProbe.Nodes.Single(), restoredShared) &&
                ReferenceEquals(restoredProbe.Lookup["shared"], restoredShared),
            "Object graph roundtrip lost polymorphism, shared identity, collections, or a cycle.");
        Require(ReferenceEquals(restoredProbe.TargetObject, restoredTarget) &&
                ReferenceEquals(restoredProbe.TargetComponent,
                    restoredTarget.GetComponent<SceneProbeBehaviour>()),
            "Object graph roundtrip did not resolve scene BObject references after graph creation.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record ObjectDomainObservation(
        Scene[] Scenes,
        GameObject[] GameObjects,
        SceneProbeBehaviour[] Behaviours,
        GameObject? NamedObject,
        ObjectDomainProbeAsset[] Assets);

    private sealed class RuntimeDomainServices : IServiceProvider
    {
        private readonly ISceneLoader? _sceneLoader;
        public RuntimeSceneManager SceneManager { get; }

        public RuntimeDomainServices(Func<string, IServiceProvider, Scene>? loadScene = null)
        {
            _sceneLoader = loadScene is null ? null : new DelegateSceneLoader(loadScene);
            SceneManager = new RuntimeSceneManager(this);
        }

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IRuntimeSceneManager) || serviceType == typeof(RuntimeSceneManager)
                ? SceneManager
                : serviceType == typeof(ISceneLoader)
                    ? _sceneLoader
                : null;
    }

    private sealed class DelegateSceneLoader(Func<string, IServiceProvider, Scene> loadScene) : ISceneLoader
    {
        public Scene LoadScene(string sceneNameOrPath, IServiceProvider services) =>
            loadScene(sceneNameOrPath, services);
    }

    private sealed class ScopedRuntimeProbeSystem : ISceneRuntimeSystem { }

    private sealed class TrackingScopeFactory : IServiceScopeFactory
    {
        public int CreateCount { get; private set; }
        public int DisposeCount { get; private set; }

        public IServiceScope CreateScope()
        {
            CreateCount++;
            return new TrackingScope(
                new ScopedRuntimeProbeSystem(), () => DisposeCount++);
        }
    }

    private sealed class TrackingScope(
        ScopedRuntimeProbeSystem system,
        Action onDispose) : IServiceScope
    {
        private bool _disposed;
        public IServiceProvider ServiceProvider { get; } = new ScopedRuntimeProbeProvider(system);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            onDispose();
        }
    }

    private sealed class ScopedRuntimeProbeProvider(ScopedRuntimeProbeSystem system) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ScopedRuntimeProbeSystem) ? system : null;
    }
}

internal sealed class ObjectDomainProbeAsset : ScriptableObject { }
