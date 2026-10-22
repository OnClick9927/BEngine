using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BEngine.Physics2D;

// Prepare the measured first-contact compilation chain while the package is
// configured. Delegates are never invoked: no world, contacts or simulation
// state are created, and no collision/trigger callbacks can run here.
internal static class PhysicsCompilationWarmup
{
    private static readonly object Gate = new();
    private static bool _prepared;
    private const BindingFlags Methods = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static void Prepare()
    {
        if (Volatile.Read(ref _prepared)) return;
        if (!OperatingSystem.IsWindows() || !RuntimeFeature.IsDynamicCodeSupported || !RuntimeFeature.IsDynamicCodeCompiled ||
            RuntimeInformation.FrameworkDescription.StartsWith("Mono", StringComparison.OrdinalIgnoreCase) ||
            Type.GetType("Mono.Runtime", throwOnError: false) is not null) return;
        lock (Gate)
        {
            if (_prepared) return;
            PhysicsWorld2D.PrepareContactCompilation();
            CollisionAlgorithms2D.PrepareContactCompilation();
            PhysicsBroadphase2D.PrepareContactCompilation();
            PhysicsWorld2DState.PrepareContactCompilation();
            PrepareKnown(typeof(PhysicsBounds2D), nameof(PhysicsBounds2D.Intersects), typeof(PhysicsBounds2D));
            PrepareKnown(typeof(Collider2D), "get_" + nameof(Collider2D.compositeOwner));
            Volatile.Write(ref _prepared, true);
        }
    }

    internal static void Prepare(Delegate target) => RuntimeHelpers.PrepareMethod(target.Method.MethodHandle);

    internal static void PrepareOpen<TDelegate>(Type owner, string method, params Type[] parameters) where TDelegate : Delegate
    {
        var target = owner.GetMethod(method, Methods, binder: null, types: parameters, modifiers: null);
        if (target is not null) Prepare(target.CreateDelegate<TDelegate>());
    }

    internal static void PrepareKnown(Type owner, string method, params Type[] parameters)
    {
        var target = owner.GetMethod(method, Methods, binder: null, types: parameters, modifiers: null);
        // Private CoreLib entry points are an optional optimization on other
        // framework versions. Their ordinary call path remains authoritative.
        if (target is not null && !target.ContainsGenericParameters) RuntimeHelpers.PrepareMethod(target.MethodHandle);
    }

    internal static void PrepareListGrowth<T>() => PrepareKnown(typeof(List<T>), "AddWithResize", typeof(T));

    internal static void PrepareSort<T>()
    {
        var definition = typeof(List<>).Assembly.GetType("System.Collections.Generic.ArraySortHelper`1", throwOnError: false);
        if (definition is null) return;
        var helper = definition.MakeGenericType(typeof(T));
        PrepareKnown(helper, "Sort", typeof(Span<T>), typeof(Comparison<T>));
        PrepareKnown(helper, "IntroSort", typeof(Span<T>), typeof(int), typeof(Comparison<T>));
        PrepareKnown(helper, "InsertionSort", typeof(Span<T>), typeof(Comparison<T>));
    }

    internal static void PrepareBuffer<T>() where T : struct
    {
        PrepareKnown(typeof(FrameDataBuffer<T>), nameof(FrameDataBuffer<T>.Add), typeof(T).MakeByRefType());
        PrepareKnown(typeof(FrameDataBuffer<T>), "get_Item", typeof(int));
    }
}

public sealed partial class PhysicsWorld2D
{
    internal static void PrepareContactCompilation()
    {
        PhysicsCompilationWarmup.PrepareOpen<Action<PhysicsWorld2D, ContactState, ContactState?, Fix64>>(
            typeof(PhysicsWorld2D), nameof(PrepareContact), typeof(ContactState), typeof(ContactState), typeof(Fix64));
        PhysicsCompilationWarmup.PrepareOpen<Action<PhysicsWorld2D, ContactState>>(
            typeof(PhysicsWorld2D), nameof(WarmStart), typeof(ContactState));
        PhysicsCompilationWarmup.PrepareOpen<Action<PhysicsWorld2D, ContactState>>(
            typeof(PhysicsWorld2D), nameof(ResolveVelocity), typeof(ContactState));
        PhysicsCompilationWarmup.PrepareOpen<Func<PhysicsWorld2D, ContactState, bool>>(
            typeof(PhysicsWorld2D), nameof(SolveNormalBlock), typeof(ContactState));
        PhysicsCompilationWarmup.PrepareOpen<Func<PhysicsWorld2D, int, ContactState[]>>(
            typeof(PhysicsWorld2D), nameof(RentContactGroup), typeof(int));
        PhysicsCompilationWarmup.PrepareOpen<Action<ContactState, Collider2D, Collider2D, PhysicsManifold2D, bool, int, int>>(
            typeof(ContactState), nameof(ContactState.Initialize), typeof(Collider2D), typeof(Collider2D), typeof(PhysicsManifold2D), typeof(bool), typeof(int), typeof(int));
        PhysicsCompilationWarmup.Prepare((Action<ContactState>)ResolvePosition);
        PhysicsCompilationWarmup.Prepare((Func<Rigidbody2D?, Fix64>)InverseMass);
        PhysicsCompilationWarmup.Prepare((Func<Rigidbody2D?, Fix64>)InverseInertia);
        PhysicsCompilationWarmup.Prepare((Func<Rigidbody2D?, Vector2, Vector2, Fix64>)EffectiveMass);
        PhysicsCompilationWarmup.Prepare((Func<Rigidbody2D?, Vector2, Vector2, Vector2>)CachedVelocity);
        PhysicsCompilationWarmup.Prepare((Action<Rigidbody2D?, Vector2, Fix64, Fix64, Vector2, Vector2>)ApplyCached);
        PhysicsCompilationWarmup.Prepare((Action<ContactState, ContactPhase>)Dispatch);
        PhysicsCompilationWarmup.Prepare((Action<Collider2D, Collider2D, ContactState[], bool, string>)InvokeCollision);
        PhysicsCompilationWarmup.Prepare((Func<Collider2D, Collider2D, ContactKey>)ContactKey.Create);
        PhysicsCompilationWarmup.Prepare((Comparison<ComponentSortEntry>)CompareComponents);
        // Queries can first occur long after contact simulation has begun.
        // Prepare their measured branch in the same actual world module.
        PhysicsCompilationWarmup.PrepareKnown(typeof(PhysicsWorld2D), nameof(RaycastInWorld),
            typeof(Vector2), typeof(Vector2), typeof(Fix64), typeof(ulong), typeof(QueryTriggerInteraction), typeof(RaycastHit2D).MakeByRefType());
        foreach (var owner in new[] { typeof(Physics2D), typeof(PhysicsWorld2D) })
            PhysicsCompilationWarmup.PrepareKnown(owner, nameof(Raycast), typeof(Vector2), typeof(Vector2),
                typeof(RaycastHit2D).MakeByRefType(), typeof(Fix64), typeof(ulong), typeof(QueryTriggerInteraction));
        // This value-returning convenience overload is a separate measured
        // method body; preparing the out/bool overload does not compile it.
        PhysicsCompilationWarmup.Prepare((Func<Vector2, Vector2, Fix64, ulong, RaycastHit2D>)Physics2D.Raycast);
        PhysicsCompilationWarmup.PrepareKnown(typeof(PhysicsWorld2D), "get_" + nameof(ActiveWorld));
        PhysicsCompilationWarmup.Prepare((Func<Component, bool>)IsActive);
        PhysicsCompilationWarmup.Prepare((Func<Collider2D, ulong, bool>)LayerMatches);
        PhysicsCompilationWarmup.PrepareOpen<Func<PhysicsWorld2D, Collider2D, QueryTriggerInteraction, bool>>(
            typeof(PhysicsWorld2D), nameof(TriggerMatches), typeof(Collider2D), typeof(QueryTriggerInteraction));
        PhysicsCompilationWarmup.Prepare((Func<Collider2D, bool>)IsQueryable);
        PhysicsCompilationWarmup.Prepare((Func<RaycastHit2D, RaycastHit2D, bool>)IsCloserHit);
        PhysicsCompilationWarmup.PrepareKnown(typeof(PhysicsWorld2D), nameof(TryLeaf), typeof(PhysicsShape2D), typeof(int), typeof(PhysicsShape2D).MakeByRefType());
        // One measured local function has compiler-generated closure-struct
        // parameters. Resolve only this exact owner/name prefix, not a package
        // or assembly scan and not an arbitrary list of gameplay methods.
        foreach (var method in typeof(PhysicsWorld2D).GetMethods(BindingFlags.NonPublic | BindingFlags.Static))
            if (method.Name.StartsWith("<PrepareContact>g__Coupling|", StringComparison.Ordinal))
                RuntimeHelpers.PrepareMethod(method.MethodHandle);
        PhysicsCompilationWarmup.PrepareBuffer<SolverPoint>();
        PhysicsCompilationWarmup.PrepareBuffer<ComponentSortEntry>();
        PhysicsCompilationWarmup.PrepareSort<ComponentSortEntry>();
        var dictionary = typeof(Dictionary<ContactKey, ContactState>);
        PhysicsCompilationWarmup.PrepareKnown(dictionary, "FindValue", typeof(ContactKey));
        PhysicsCompilationWarmup.PrepareKnown(dictionary, "Initialize", typeof(int));
        PhysicsCompilationWarmup.PrepareKnown(dictionary, "Resize", typeof(int), typeof(bool));
        var insertion = typeof(Dictionary<,>).Assembly.GetType("System.Collections.Generic.InsertionBehavior", throwOnError: false);
        if (insertion is not null)
            PhysicsCompilationWarmup.PrepareKnown(dictionary, "TryInsert", typeof(ContactKey), typeof(ContactState), insertion);
    }
}

internal static partial class CollisionAlgorithms2D
{
    private delegate bool CollidePreparation(PhysicsShape2D left, PhysicsShape2D right, out PhysicsManifold2D manifold);
    private delegate bool AxesPreparation(PhysicsShape2D left, PhysicsShape2D right, PhysicsShape2D source, ref Vector2 normal, ref Fix64 penetration);
    private delegate bool AxisPreparation(PhysicsShape2D left, PhysicsShape2D right, Vector2 axis, ref Vector2 normal, ref Fix64 penetration);
    private delegate void ProjectPreparation(PhysicsShape2D shape, Vector2 axis, out Fix64 minimum, out Fix64 maximum);
    private delegate int ClipPreparation(ReadOnlySpan<Vector2> points, Vector2 normal, Fix64 limit, Span<Vector2> result);
    private delegate bool RayPreparation(PhysicsShape2D shape, Vector2 origin, Vector2 direction, Fix64 maxDistance, out Fix64 distance, out Vector2 normal);

    internal static void PrepareContactCompilation()
    {
        PhysicsCompilationWarmup.Prepare((CollidePreparation)TryCollide);
        PhysicsCompilationWarmup.Prepare((CollidePreparation)PolygonPolygon);
        PhysicsCompilationWarmup.Prepare((AxesPreparation)TestPolygonAxes);
        PhysicsCompilationWarmup.Prepare((AxisPreparation)TestAxis);
        PhysicsCompilationWarmup.Prepare((ProjectPreparation)Project);
        PhysicsCompilationWarmup.Prepare((Func<PhysicsShape2D, PhysicsShape2D, Vector2, Fix64, PhysicsManifold2D>)ClipContactEdges);
        PhysicsCompilationWarmup.Prepare((Func<PhysicsShape2D, Vector2, int>)FacingEdge);
        PhysicsCompilationWarmup.Prepare((Func<PhysicsShape2D, int, Vector2>)Outward);
        PhysicsCompilationWarmup.Prepare((ClipPreparation)ClipSegment);
        PhysicsCompilationWarmup.Prepare((RayPreparation)Raycast);
        PhysicsCompilationWarmup.Prepare((RayPreparation)RaycastCircle);
        PhysicsCompilationWarmup.PrepareKnown(typeof(CollisionAlgorithms2D), nameof(RaySegment),
            typeof(Vector2), typeof(Vector2), typeof(Vector2), typeof(Vector2), typeof(Fix64).MakeByRefType());
        PhysicsCompilationWarmup.Prepare((Func<PhysicsShape2D, Vector2, bool>)Contains);
        PhysicsCompilationWarmup.Prepare((Func<Vector2, Vector2, Fix64>)Cross);
        ContactCollector.PrepareContactCompilation();
        PhysicsCompilationWarmup.PrepareListGrowth<ShapeContact>();
    }

    internal sealed partial class ContactCollector
    {
        internal static void PrepareContactCompilation()
        {
            PhysicsCompilationWarmup.PrepareOpen<Func<ContactCollector, PhysicsShape2D, PhysicsShape2D, List<ShapeContact>>>(
                typeof(ContactCollector), nameof(Collect), typeof(PhysicsShape2D), typeof(PhysicsShape2D));
            PhysicsCompilationWarmup.Prepare((Action<PhysicsShape2D, List<PhysicsShape2D>>)AddLeaves);
        }
    }
}

internal sealed partial class PhysicsBroadphase2D
{
    internal static void PrepareContactCompilation()
    {
        PhysicsCompilationWarmup.Prepare(ProxyComparison);
        PhysicsCompilationWarmup.Prepare(PairComparison);
        PhysicsCompilationWarmup.PrepareListGrowth<Proxy>();
        PhysicsCompilationWarmup.PrepareSort<Proxy>();
        // Candidate pairs use a different closed value-type list/sorter than
        // the proxies. Its first growth/sort was still compiling on contact.
        PhysicsCompilationWarmup.PrepareListGrowth<(int Left, int Right)>();
        // The measured closed pair list still entered these non-inlined
        // capacity helpers on its first real growth after AddWithResize.
        PhysicsCompilationWarmup.PrepareKnown(typeof(List<(int Left, int Right)>), "Grow", typeof(int));
        PhysicsCompilationWarmup.PrepareKnown(typeof(List<(int Left, int Right)>), "GetNewCapacity", typeof(int));
        PhysicsCompilationWarmup.PrepareSort<(int Left, int Right)>();
    }
}

internal sealed partial class PhysicsWorld2DState
{
    internal static void PrepareContactCompilation()
    {
        PhysicsCompilationWarmup.PrepareOpen<Func<PhysicsWorld2DState, Collider2D, Collider2D, bool>>(
            typeof(PhysicsWorld2DState), nameof(ShouldCollide), typeof(Collider2D), typeof(Collider2D));
        PhysicsCompilationWarmup.Prepare((Func<Collider2D, Collider2D, CollisionPair>)CollisionPair.Create);
        PhysicsCompilationWarmup.PrepareKnown(typeof(HashSet<CollisionPair>), "FindItemIndex", typeof(CollisionPair));
        PhysicsCompilationWarmup.PrepareKnown(typeof(HashSet<LayerPair>), "FindItemIndex", typeof(LayerPair));
    }
}
