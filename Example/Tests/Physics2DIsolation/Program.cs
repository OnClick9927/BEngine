using BEngine.Physics2D;
using BEngine.Serialization;
using Physics = BEngine.Physics2D.Physics2D;

namespace BEngine.ExampleTests.Physics2DIsolation;

internal static class Program
{
    private const string RuntimeSystemId = "physics2d-scene-state-isolation";
    private static readonly Vector2 DefaultGravity = new(0, -12);
    private static readonly PhysicsSnapshot DefaultSnapshot = new(DefaultGravity, true, true, 6, false, 2);
    private static readonly PhysicsSnapshot SceneASnapshot = new(new Vector2(1, -3), false, false, 8, true, 0);
    private static readonly PhysicsSnapshot SceneBSnapshot = new(new Vector2(-4, 5), true, true, 12, false, 2);

    private static int Main()
    {
        try
        {
            RuntimeTypeCache.Warmup();
            RuntimePackageState.SetEnabled("com.bengine.physics2d", true);
            RuntimeSystemRegistry.Register(RuntimeSystemId, static () => new PhysicsWorld2D());
            try { VerifySceneAndRestartIsolation(); }
            finally { RuntimeSystemRegistry.Unregister(RuntimeSystemId); }

            Console.WriteLine(
                "PHYSICS2D_ISOLATION_OK|per-scene-settings,per-scene-ignore,default-domain,restart-reset");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PHYSICS2D_ISOLATION_FAILED|{exception}");
            return 1;
        }
    }

    private static void VerifySceneAndRestartIsolation()
    {
        ConfigureDefaultState();
        using var source = CreateSourceScene();
        var sourcePair = GetPair(source);
        Physics.IgnoreCollision(sourcePair.Left, sourcePair.Right);

        var sceneSnapshot = SceneAssetSerialization.Serialize(source);
        using var sceneA = SceneAssetSerialization.Deserialize(sceneSnapshot);
        using var sceneB = SceneAssetSerialization.Deserialize(sceneSnapshot);
        var pairA = GetPair(sceneA);
        var pairB = GetPair(sceneB);

        Require(pairA.Left.Id == pairB.Left.Id && pairA.Right.Id == pairB.Right.Id,
            "The regression fixture did not preserve collider Guids across the two Scene mirrors.");
        Require(pairA.Left.GetInstanceID() != pairB.Left.GetInstanceID() &&
                pairA.Right.GetInstanceID() != pairB.Right.GetInstanceID(),
            "The regression fixture did not create distinct runtime collider instances.");

        var probeA = AddProbe(sceneA, pairA, SceneASnapshot, ignoreCollision: true);
        var probeB = AddProbe(sceneB, pairB, SceneBSnapshot, ignoreCollision: false);
        var runtimeA = new SceneRuntime(sceneA);
        var runtimeB = new SceneRuntime(sceneB);
        try
        {
            runtimeA.Start();
            Require(PhysicsWorld2D.Get(sceneA) is not null,
                "Starting Scene A did not create its PhysicsWorld2D.");
            AssertSnapshot(DefaultSnapshot, probeA.BeforeConfiguration,
                "Scene A did not start from the independent non-runtime defaults.");
            AssertSnapshot(SceneASnapshot, probeA.AfterConfiguration,
                "Scene A did not retain its configured physics state.");

            runtimeB.Start();
            Require(PhysicsWorld2D.Get(sceneB) is not null &&
                    !ReferenceEquals(PhysicsWorld2D.Get(sceneA), PhysicsWorld2D.Get(sceneB)),
                "Two running Scenes shared one PhysicsWorld2D instance.");
            AssertSnapshot(DefaultSnapshot, probeB.BeforeConfiguration,
                "Scene B inherited Scene A's physics state.");
            AssertSnapshot(SceneBSnapshot, probeB.AfterConfiguration,
                "Scene B did not retain its configured physics state.");

            runtimeA.Tick(Fix64.Parse("0.001"));
            runtimeB.Tick(Fix64.Parse("0.001"));
            AssertSnapshot(SceneASnapshot, probeA.DuringUpdate,
                "Scene A observed Scene B's settings or ignored-collision pairs.");
            AssertSnapshot(SceneBSnapshot, probeB.DuringUpdate,
                "Scene B observed Scene A's settings or ignored-collision pairs.");

            runtimeB.Stop();
            AssertSnapshot(SceneASnapshot, PhysicsSnapshot.Capture(pairA.Left, pairA.Right),
                "Stopping the active Scene did not restore the remaining active physics world.");
        }
        finally
        {
            runtimeB.Stop();
            runtimeA.Stop();
        }

        Require(PhysicsWorld2D.Get(sceneA) is null && PhysicsWorld2D.Get(sceneB) is null,
            "Stopping the runtimes left a PhysicsWorld2D registered for a Scene.");
        AssertGlobalDefaults(sourcePair);

        probeA.ConfigureOnAwake = false;
        probeA.IgnoreCollisionOnAwake = null;
        probeA.ClearObservations();
        var restartedRuntime = new SceneRuntime(sceneA);
        try
        {
            restartedRuntime.Start();
            AssertSnapshot(DefaultSnapshot, probeA.BeforeConfiguration,
                "Restarting Scene A restored settings from the previous runtime.");
            AssertSnapshot(DefaultSnapshot, probeA.AfterConfiguration,
                "Restarting Scene A restored an ignored-collision pair from the previous runtime.");
            restartedRuntime.Tick(Fix64.Parse("0.001"));
            AssertSnapshot(DefaultSnapshot, probeA.DuringUpdate,
                "Restarted Scene A did not remain on a clean per-Scene state.");
        }
        finally { restartedRuntime.Stop(); }

        AssertGlobalDefaults(sourcePair);
    }

    private static void ConfigureDefaultState()
    {
        Physics.gravity = DefaultGravity;
        Physics.queriesHitTriggers = true;
        Physics.autoSimulation = true;
        Physics.velocityIterations = 6;
    }

    private static Scene CreateSourceScene()
    {
        var scene = new Scene("Physics isolation source");
        scene.CreateGameObject("Left Collider").AddComponent<BoxCollider2D>().isTrigger = true;
        scene.CreateGameObject("Right Collider").AddComponent<BoxCollider2D>().isTrigger = true;
        return scene;
    }

    private static ColliderPair GetPair(Scene scene)
    {
        var left = scene.Find("Left Collider")?.GetComponent<BoxCollider2D>() ??
                   throw new InvalidOperationException("The left test collider is missing.");
        var right = scene.Find("Right Collider")?.GetComponent<BoxCollider2D>() ??
                    throw new InvalidOperationException("The right test collider is missing.");
        return new ColliderPair(left, right);
    }

    private static PhysicsIsolationProbe AddProbe(
        Scene scene,
        ColliderPair pair,
        PhysicsSnapshot desired,
        bool ignoreCollision)
    {
        var probe = scene.CreateGameObject("Physics State Probe").AddComponent<PhysicsIsolationProbe>();
        probe.Left = pair.Left;
        probe.Right = pair.Right;
        probe.Desired = desired;
        probe.ConfigureOnAwake = true;
        probe.IgnoreCollisionOnAwake = ignoreCollision;
        return probe;
    }

    private static void AssertGlobalDefaults(ColliderPair sourcePair)
    {
        var actual = PhysicsSnapshot.Capture(sourcePair.Left, sourcePair.Right);
        AssertSnapshot(DefaultSnapshot with { IgnoreCollision = true, QueryHitCount = 0 }, actual,
            "Runtime physics state leaked into the independent non-runtime defaults.");
    }

    private static void AssertSnapshot(PhysicsSnapshot expected, PhysicsSnapshot? actual, string message)
    {
        Require(actual is not null, $"{message} No observation was captured.");
        Require(expected == actual, $"{message} Expected {expected}; actual {actual}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private readonly record struct ColliderPair(BoxCollider2D Left, BoxCollider2D Right);
}

internal sealed class PhysicsIsolationProbe : MonoBehaviour
{
    public BoxCollider2D Left { get; set; } = null!;
    public BoxCollider2D Right { get; set; } = null!;
    public PhysicsSnapshot Desired { get; set; }
    public bool ConfigureOnAwake { get; set; }
    public bool? IgnoreCollisionOnAwake { get; set; }
    public PhysicsSnapshot? BeforeConfiguration { get; private set; }
    public PhysicsSnapshot? AfterConfiguration { get; private set; }
    public PhysicsSnapshot? DuringUpdate { get; private set; }

    public override void Awake()
    {
        BeforeConfiguration = PhysicsSnapshot.Capture(Left, Right);
        if (ConfigureOnAwake)
        {
            Physics.gravity = Desired.Gravity;
            Physics.queriesHitTriggers = Desired.QueriesHitTriggers;
            Physics.autoSimulation = Desired.AutoSimulation;
            Physics.velocityIterations = Desired.VelocityIterations;
        }
        if (IgnoreCollisionOnAwake is { } ignore)
            Physics.IgnoreCollision(Left, Right, ignore);
        AfterConfiguration = PhysicsSnapshot.Capture(Left, Right);
    }

    public override void Update() => DuringUpdate = PhysicsSnapshot.Capture(Left, Right);

    public void ClearObservations()
    {
        BeforeConfiguration = null;
        AfterConfiguration = null;
        DuringUpdate = null;
    }
}

internal readonly record struct PhysicsSnapshot(
    Vector2 Gravity,
    bool QueriesHitTriggers,
    bool AutoSimulation,
    int VelocityIterations,
    bool IgnoreCollision,
    int QueryHitCount)
{
    public static PhysicsSnapshot Capture(BoxCollider2D left, BoxCollider2D right) => new(
        Physics.gravity,
        Physics.queriesHitTriggers,
        Physics.autoSimulation,
        Physics.velocityIterations,
        Physics.GetIgnoreCollision(left, right),
        Physics.OverlapBox(Vector2.zero, new Vector2(4, 4)).Length);
}
