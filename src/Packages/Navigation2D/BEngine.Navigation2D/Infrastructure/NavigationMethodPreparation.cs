using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BEngine.Navigation2D;

// The first blocked route can be queried long after a surface has been baked.
// Prepare its measured compilation chain at that bake boundary. No search,
// scene query, path mutation or navigation revision is performed here.
internal static class NavigationMethodPreparation
{
    private static readonly object Gate = new();
    private static bool _prepared;
    private const BindingFlags Methods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    internal static void Prepare()
    {
        if (Volatile.Read(ref _prepared)) return;
        if (!OperatingSystem.IsWindows() || !RuntimeFeature.IsDynamicCodeSupported || !RuntimeFeature.IsDynamicCodeCompiled ||
            RuntimeInformation.FrameworkDescription.StartsWith("Mono", StringComparison.OrdinalIgnoreCase) ||
            Type.GetType("Mono.Runtime", false) is not null) return;
        lock (Gate)
        {
            if (_prepared) return;
            RuntimeHelpers.PrepareMethod(((Func<Vector2, Vector2, ulong, NavigationPath2D, bool>)Navigation2D.CalculatePath).Method.MethodHandle);
            RuntimeHelpers.PrepareMethod(((Func<Vector2, Vector2, ulong, NavigationPath2D, int?, bool>)Navigation2D.CalculatePath).Method.MethodHandle);
            NavigationGrid2D.PrepareSearchCompilation();
            Volatile.Write(ref _prepared, true);
        }
    }

    internal static void PrepareKnown(Type? owner, string name, params Type[] parameters)
    {
        var method = owner?.GetMethod(name, Methods, binder: null, types: parameters, modifiers: null);
        // A different CoreLib or a trimmed unused branch may omit a helper.
        // Ordinary navigation remains authoritative when it is absent.
        if (method is not null && !method.ContainsGenericParameters) RuntimeHelpers.PrepareMethod(method.MethodHandle);
    }

    internal static void PrepareConstructor(Type owner, params Type[] parameters)
    {
        var constructor = owner.GetConstructor(Methods, binder: null, types: parameters, modifiers: null);
        if (constructor is not null) RuntimeHelpers.PrepareMethod(constructor.MethodHandle);
    }

    internal static void PrepareIterator(Type owner, string name, params Type[] parameters)
    {
        var method = owner.GetMethod(name, Methods, binder: null, types: parameters, modifiers: null);
        if (method is null) return;
        RuntimeHelpers.PrepareMethod(method.MethodHandle);
        var iterator = method.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
        if (iterator is null) return;
        PrepareConstructor(iterator, typeof(int));
        foreach (var body in iterator.GetMethods(Methods | BindingFlags.DeclaredOnly))
            if (body.Name == "MoveNext" || body.Name.EndsWith(".GetEnumerator", StringComparison.Ordinal) ||
                body.Name.EndsWith(".get_Current", StringComparison.Ordinal) || body.Name.EndsWith(".Dispose", StringComparison.Ordinal) ||
                body.Name.StartsWith("<>m__Finally", StringComparison.Ordinal))
            {
                RuntimeHelpers.PrepareMethod(body.MethodHandle);
                // PrepareMethod may retain the virtual entry precode. An open
                // delegate prepares its real target without making an iterator
                // or invoking MoveNext/Current/Dispose.
                if (body.IsVirtual && body.GetParameters().Length == 0)
                {
                    var signature = body.ReturnType == typeof(void)
                        ? typeof(Action<>).MakeGenericType(iterator)
                        : typeof(Func<,>).MakeGenericType(iterator, body.ReturnType);
                    RuntimeHelpers.PrepareDelegate(body.CreateDelegate(signature));
                }
            }
    }

    internal static void PrepareArray<T>(string name, int parameterCount)
    {
        foreach (var method in typeof(Array).GetMethods(BindingFlags.Public | BindingFlags.Static))
            if (method.Name == name && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 &&
                method.GetParameters().Length == parameterCount)
                RuntimeHelpers.PrepareMethod(method.MakeGenericMethod(typeof(T)).MethodHandle);
    }

    internal static void PrepareDictionary<TKey, TValue>() where TKey : notnull
    {
        var owner = typeof(Dictionary<TKey, TValue>);
        PrepareConstructor(owner, typeof(int), typeof(IEqualityComparer<TKey>));
        PrepareKnown(owner, "FindValue", typeof(TKey));
        PrepareKnown(owner, "Initialize", typeof(int));
        PrepareKnown(owner, "Resize", typeof(int), typeof(bool));
        var insertion = owner.Assembly.GetType("System.Collections.Generic.InsertionBehavior", false);
        if (insertion is not null) PrepareKnown(owner, "TryInsert", typeof(TKey), typeof(TValue), insertion);
    }
}

internal sealed partial class NavigationGrid2D
{
    internal static void PrepareSearchCompilation()
    {
        const BindingFlags methods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var owner = typeof(NavigationGrid2D);
        NavigationMethodPreparation.PrepareKnown(owner, nameof(CalculatePath), typeof(Vector2), typeof(Vector2), typeof(NavigationPath2D), typeof(ulong));
        NavigationMethodPreparation.PrepareIterator(owner, nameof(Search), typeof(Vector2), typeof(Vector2), typeof(NavigationPath2D), typeof(ulong));
        NavigationMethodPreparation.PrepareKnown(owner, nameof(Neighbours), typeof(Cell), typeof(ulong), typeof(List<LinkEdge>), typeof(Vector2), typeof(Vector2));
        NavigationMethodPreparation.PrepareConstructor(typeof(NeighbourEnumerable), owner, typeof(Cell), typeof(ulong), typeof(List<LinkEdge>), typeof(Vector2), typeof(Vector2));
        NavigationMethodPreparation.PrepareKnown(typeof(NeighbourEnumerable), nameof(NeighbourEnumerable.GetEnumerator));
        NavigationMethodPreparation.PrepareConstructor(typeof(NeighbourEnumerator), owner, typeof(Cell), typeof(ulong), typeof(List<LinkEdge>), typeof(Vector2), typeof(Vector2));
        NavigationMethodPreparation.PrepareKnown(typeof(NeighbourEnumerator), nameof(NeighbourEnumerator.MoveNext));
        NavigationMethodPreparation.PrepareKnown(typeof(NeighbourEnumerator), "get_Current");
        NavigationMethodPreparation.PrepareKnown(typeof(NeighbourEnumerator), "set_Current", typeof((Cell, Fix64)));
        NavigationMethodPreparation.PrepareKnown(typeof(NeighbourEnumerator), nameof(NeighbourEnumerator.Dispose));
        NavigationMethodPreparation.PrepareKnown(owner, nameof(ToCell), typeof(Vector2));
        NavigationMethodPreparation.PrepareKnown(owner, nameof(FindNearest), typeof(Cell), typeof(ulong));
        NavigationMethodPreparation.PrepareKnown(owner, nameof(IsWalkable), typeof(Cell), typeof(ulong));
        NavigationMethodPreparation.PrepareKnown(owner, nameof(InBounds), typeof(Cell));
        NavigationMethodPreparation.PrepareKnown(owner, nameof(Simplify), typeof(Vector2[]));

        // Search captures its local coordinate conversion in a compiler-owned
        // display type. Restrict this to this owner and this source local name.
        foreach (var display in owner.GetNestedTypes(BindingFlags.NonPublic))
            if (display.Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal))
                foreach (var body in display.GetMethods(methods | BindingFlags.DeclaredOnly))
                    if (body.Name.StartsWith("<Search>g__World|", StringComparison.Ordinal) &&
                        body.ReturnType == typeof(Vector2) && body.GetParameters() is [{ ParameterType: var parameter }] && parameter == typeof(Cell))
                        RuntimeHelpers.PrepareMethod(body.MethodHandle);

        NavigationMethodPreparation.PrepareDictionary<Cell, Cell>();
        NavigationMethodPreparation.PrepareDictionary<Cell, Fix64>();
        var set = typeof(HashSet<Cell>);
        NavigationMethodPreparation.PrepareKnown(set, "AddIfNotPresent", typeof(Cell), typeof(int).MakeByRefType());
        NavigationMethodPreparation.PrepareKnown(set, "FindItemIndex", typeof(Cell));
        NavigationMethodPreparation.PrepareKnown(set, "Initialize", typeof(int));
        NavigationMethodPreparation.PrepareKnown(set, "Resize", typeof(int), typeof(bool));
        var queue = typeof(PriorityQueue<Cell, (long Cost, int Y, int X)>);
        NavigationMethodPreparation.PrepareKnown(queue, nameof(PriorityQueue<Cell, (long, int, int)>.Enqueue), typeof(Cell), typeof((long, int, int)));
        NavigationMethodPreparation.PrepareKnown(queue, "Grow", typeof(int));
        NavigationMethodPreparation.PrepareKnown(queue, "RemoveRootNode");
        NavigationMethodPreparation.PrepareKnown(queue, "MoveDownDefaultComparer", typeof((Cell, (long, int, int))), typeof(int));
        NavigationMethodPreparation.PrepareKnown(typeof(List<Cell>), "AddWithResize", typeof(Cell));
        NavigationMethodPreparation.PrepareKnown(typeof(List<Cell>), nameof(List<Cell>.Reverse), typeof(int), typeof(int));
        NavigationMethodPreparation.PrepareKnown(typeof(List<LinkEdge>.Enumerator), nameof(List<LinkEdge>.Enumerator.MoveNext));
        NavigationMethodPreparation.PrepareArray<(Cell, (long, int, int))>(nameof(Array.Resize), 2);
        NavigationMethodPreparation.PrepareArray<Cell>(nameof(Array.Reverse), 3);
        var spanHelpers = typeof(Array).Assembly.GetType("System.SpanHelpers", false);
        NavigationMethodPreparation.PrepareKnown(spanHelpers, "Reverse", typeof(long).MakeByRefType(), typeof(nuint));
        if (spanHelpers is not null)
            foreach (var method in spanHelpers.GetMethods(methods))
                if (method.Name == "ReverseInner" && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 &&
                    method.GetParameters() is [{ ParameterType: var first }, { ParameterType: var second }] &&
                    first.IsByRef && second == typeof(nuint))
                    RuntimeHelpers.PrepareMethod(method.MakeGenericMethod(typeof(Cell)).MethodHandle);

        foreach (var method in typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (!method.IsGenericMethodDefinition) continue;
            if (method.Name == nameof(Enumerable.Select) && method.GetGenericArguments().Length == 2 &&
                method.GetParameters() is [_, { ParameterType: var selector }] && selector.IsGenericType && selector.GetGenericTypeDefinition() == typeof(Func<,>))
                RuntimeHelpers.PrepareMethod(method.MakeGenericMethod(typeof(Cell), typeof(Vector2)).MethodHandle);
            else if (method.Name == nameof(Enumerable.ToArray) && method.GetGenericArguments().Length == 1 && method.GetParameters().Length == 1)
                RuntimeHelpers.PrepareMethod(method.MakeGenericMethod(typeof(Vector2)).MethodHandle);
        }
        var select = typeof(Enumerable).Assembly.GetType("System.Linq.Enumerable+ListSelectIterator`2", false);
        if (select is not null) NavigationMethodPreparation.PrepareKnown(select.MakeGenericType(typeof(Cell), typeof(Vector2)), nameof(Enumerable.ToArray));
        NavigationMethodPreparation.PrepareKnown(typeof(List<Vector2>), nameof(List<Vector2>.ToArray));

        // These measured CoreLib initializers create only their empty arrays
        // and primitive tuple comparer, never scene objects or a search state.
        RuntimeHelpers.RunClassConstructor(typeof(List<Cell>).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(List<LinkEdge>).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(Comparer<(long, int, int)>).TypeHandle);
        var emptyArray = typeof(Array).GetNestedType("EmptyArray`1", BindingFlags.NonPublic);
        if (emptyArray is not null) RuntimeHelpers.RunClassConstructor(emptyArray.MakeGenericType(typeof((Cell, (long, int, int)))).TypeHandle);
    }
}
