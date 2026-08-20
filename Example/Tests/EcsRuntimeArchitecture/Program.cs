using BEngine.Documents;
using BEngine.Entities;

namespace BEngine.ExampleTests.EcsRuntimeArchitecture;

internal static class Program
{
    private static int Main()
    {
        try
        {
            VerifyEntityVersions();
            VerifyComponentDataAndQueries();
            VerifySteadyStateQueryAllocations();
            VerifySceneManagedComponentBridge();
            VerifyRuntimeSystemLifecycle();
            VerifySceneSerializationRoundtrip();
            global::System.Console.WriteLine(
                "ECS_RUNTIME_ARCHITECTURE_OK|entity-version,component-data,query,command-buffer,zero-allocation-query,gameobject-bridge," +
                "destroy,systems,monobehaviour,yaml-roundtrip");
            return 0;
        }
        catch (Exception exception)
        {
            global::System.Console.Error.WriteLine($"ECS_RUNTIME_ARCHITECTURE_FAILED|{exception}");
            return 1;
        }
    }

    private static void VerifyEntityVersions()
    {
        using var world = new World("Entity version test");
        var manager = world.EntityManager;
        var first = manager.CreateEntity();
        Require(manager.Exists(first), "A newly-created entity is not alive.");
        Require(manager.DestroyEntity(first), "DestroyEntity rejected a live entity.");
        Require(!manager.Exists(first), "A destroyed entity handle remained valid.");

        var recycled = manager.CreateEntity();
        Require(recycled.Index == first.Index, "The free entity slot was not recycled.");
        Require(recycled.Version != first.Version,
            "A recycled entity slot did not advance its generation/version.");
        Require(!manager.Exists(first) && manager.Exists(recycled),
            "A stale handle aliases the entity that reused its slot.");
    }

    private static void VerifyComponentDataAndQueries()
    {
        using var world = new World("Component data test");
        var manager = world.EntityManager;
        var moving = manager.CreateEntity(new PositionData { X = 1, Y = 2 });
        var stationary = manager.CreateEntity(new PositionData { X = 10, Y = 20 });
        var velocityOnly = manager.CreateEntity(new VelocityData { X = 3, Y = 4 });
        manager.AddComponentData(moving, new VelocityData { X = 5, Y = 6 });

        Require(manager.HasComponent<PositionData>(moving), "AddComponentData did not update component presence.");
        Require(manager.GetComponentData<PositionData>(moving).X == 1,
            "GetComponentData returned the wrong value.");
        manager.SetComponentData(moving, new PositionData { X = 7, Y = 8 });
        ref var writable = ref manager.GetComponentDataRW<PositionData>(moving);
        writable.Y = 9;
        Require(manager.GetComponentData<PositionData>(moving) is { X: 7, Y: 9 },
            "SetComponentData or GetComponentDataRW did not persist a struct value.");

        var withoutVelocity = manager.CreateEntityQuery(
            ComponentType.ReadOnly<PositionData>(), ComponentType.Exclude<VelocityData>());
        Require(withoutVelocity.Count == 1 && withoutVelocity.ToEntityArray()[0] == stationary,
            "Required/excluded component filtering returned the wrong entity set.");

        Require(manager.RemoveComponent<VelocityData>(moving), "RemoveComponent rejected an existing component.");
        var enumerated = new List<Entity>();
        foreach (var entity in withoutVelocity) enumerated.Add(entity);
        Require(enumerated.Count == 2 && enumerated.Contains(moving) && enumerated.Contains(stationary),
            "A cached query did not refresh after a structural change.");
        Require(!enumerated.Contains(velocityOnly), "A query returned an entity missing its required component.");

        Require(manager.RemoveComponent<PositionData>(stationary),
            "RemoveComponent rejected a required component.");
        Require(withoutVelocity.Count == 1 && withoutVelocity.ToEntityArray()[0] == moving,
            "A query retained an entity after its required component was removed.");
    }

    private static void VerifySceneManagedComponentBridge()
    {
        var scene = new Scene("Managed bridge test");
        var gameObject = scene.CreateGameObject("Bridge");
        var entity = gameObject.entity;
        var manager = scene.world.EntityManager;

        Require(manager.EntityCount == 1 && manager.Exists(entity),
            "Scene.CreateGameObject did not create an ECS entity.");
        Require(ReferenceEquals(manager.GetManagedComponent<GameObject>(entity), gameObject),
            "The GameObject facade is not registered against its ECS entity.");
        Require(ReferenceEquals(manager.GetManagedComponent<Transform>(entity), gameObject.transform),
            "The mandatory Transform is not mirrored into ECS managed storage.");

        var behaviour = gameObject.AddComponent<EcsProbeBehaviour>();
        Require(ReferenceEquals(gameObject.GetComponent<EcsProbeBehaviour>(), behaviour),
            "GameObject.GetComponent did not read the ECS managed-component store.");
        Require(manager.QueryManagedComponents<EcsProbeBehaviour>().ToArray() is [var found] &&
                ReferenceEquals(found, behaviour),
            "Managed component queries did not expose a newly-added MonoBehaviour.");
        var facadeQuery = manager.CreateEntityQuery(
            ComponentType.ReadOnly<GameObject>(), ComponentType.ReadOnly<Transform>(),
            ComponentType.ReadOnly<EcsProbeBehaviour>());
        Require(facadeQuery.Count == 1 && facadeQuery.ToEntityArray()[0] == entity,
            "EntityQuery cannot match the Unity-style managed facade.");

        Require(gameObject.RemoveComponent(behaviour), "GameObject.RemoveComponent rejected its own component.");
        Require(manager.QueryManagedComponents<EcsProbeBehaviour>().Count == 0 && facadeQuery.Count == 0,
            "Removing a Component left a stale ECS managed-component entry.");

        var replacement = gameObject.AddComponent<EcsProbeBehaviour>();
        Require(manager.DestroyEntity(entity), "DestroyEntity could not destroy a Scene-owned entity.");
        Require(scene.Find("Bridge") is null && gameObject.entity.IsNull && !manager.Exists(entity),
            "ECS destruction did not remove and unbind the GameObject facade.");
        Require(replacement.DestroyCount == 1,
            "Destroying a Scene-owned entity did not run MonoBehaviour destruction.");
    }

    private static void VerifySteadyStateQueryAllocations()
    {
        using var world = new World("Query allocation test");
        var manager = world.EntityManager;
        for (var index = 0; index < 10_000; index++)
            manager.CreateEntity(new PositionData { X = index, Y = index });
        var query = manager.CreateEntityQuery(ComponentType.ReadWrite<PositionData>());
        _ = query.Count;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        long checksum = 0;
        for (var frame = 0; frame < 300; frame++)
        foreach (var entity in query)
            checksum += manager.GetComponentData<PositionData>(entity).X;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Require(checksum > 0, "The steady-state ECS query did not enumerate component data.");
        Require(allocated <= 1_024,
            $"A stable ECS query allocated {allocated} bytes across 300 frames.");
    }

    private static void VerifyRuntimeSystemLifecycle()
    {
        var scene = new Scene("System lifecycle test");
        var gameObject = scene.CreateGameObject("Behaviour");
        var behaviour = gameObject.AddComponent<EcsProbeBehaviour>();
        var dataEntity = scene.world.EntityManager.CreateEntity(
            new PositionData { X = 20, Y = 30 });
        var system = new EcsProbeSystem();
        scene.world.SimulationSystemGroup.AddSystem(system);

        var runtime = new SceneRuntime(scene);
        runtime.Start();
        runtime.Tick(Fix64.Parse("0.02"));
        runtime.Stop();

        Require(ReferenceEquals(system.ObservedWorld, scene.world),
            "SystemState did not expose the Scene's World.");
        Require(system.Calls.SequenceEqual(["Create", "Start", "FixedUpdate", "Update", "Stop"]),
            $"The ECS system lifecycle order was wrong: {string.Join(", ", system.Calls)}");
        Require(system.UpdatedEntities == 1 &&
                scene.world.EntityManager.GetComponentData<PositionData>(dataEntity).X == 21 &&
                scene.world.EntityManager.HasComponent<VelocityData>(dataEntity),
            "SimulationSystemGroup did not execute an ECS query/update during SceneRuntime.Tick.");
        Require(behaviour.StartCount == 1 && behaviour.UpdateCount == 1,
            "The GameObject/MonoBehaviour compatibility lifecycle was not driven by the ECS-backed runtime.");

        Require(scene.world.SimulationSystemGroup.RemoveSystem(system),
            "SimulationSystemGroup could not remove a registered system.");
        Require(system.Calls[^1] == "Destroy", "Removing a stopped ECS system did not invoke OnDestroy.");
    }

    private static void VerifySceneSerializationRoundtrip()
    {
        RuntimeTypeCache.Warmup();
        var source = new Scene("ECS YAML roundtrip");
        var gameObject = source.CreateGameObject("Serialized facade");
        gameObject.transform.localPosition = new Vector2(1, 2);
        gameObject.AddComponent<EcsProbeBehaviour>().Value = 42;

        var directory = Path.Combine(Path.GetTempPath(), $"BEngineEcs_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Ecs.scene.yaml");
            Document.SaveBObject<SceneDocument>(source, path);
            var restored = Document.LoadBObject<SceneDocument, Scene>(path);
            var restoredObject = restored.Find("Serialized facade") ??
                                 throw new InvalidOperationException("YAML lost the GameObject facade.");
            var restoredBehaviour = restoredObject.GetComponent<EcsProbeBehaviour>();

            Require(restoredBehaviour?.Value == 42 &&
                    restoredObject.transform.localPosition == new Vector2(1, 2),
                "YAML roundtrip lost serialized Component or Transform state.");
            Require(restored.world.EntityManager.Exists(restoredObject.entity) &&
                    ReferenceEquals(restored.world.EntityManager.GetManagedComponent<GameObject>(
                        restoredObject.entity), restoredObject) &&
                    ReferenceEquals(restored.world.EntityManager.GetManagedComponent<EcsProbeBehaviour>(
                        restoredObject.entity), restoredBehaviour),
                "YAML roundtrip restored objects without rebuilding their ECS mapping.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
