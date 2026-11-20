using System.Linq.Expressions;
using System.Reflection;

namespace BEngine.Editor;

internal static class ComponentContextMenuRegistry
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<Type, ComponentContextMenuCommand[]> Commands = [];
    private static AttributedMethod[] _methods = [];
    private static int _generation = -1;

    internal static void Warmup()
    {
        lock (Gate)
        {
            EnsureFresh();
            foreach (var type in TypeCache.GetTypesDerivedFrom<Component>())
                if (!type.IsAbstract) _ = GetCommandsCore(type);
        }
    }

    internal static ComponentContextMenuCommand[] GetCommands(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        if (!typeof(Component).IsAssignableFrom(componentType))
            throw new ArgumentException($"{componentType.FullName} is not a Component type.", nameof(componentType));
        lock (Gate)
        {
            EnsureFresh();
            return GetCommandsCore(componentType);
        }
    }

    private static ComponentContextMenuCommand[] GetCommandsCore(Type componentType)
    {
        if (Commands.TryGetValue(componentType, out var cached)) return cached;
        var candidates = new Dictionary<string, (AttributedMethod Item, int Distance)>(StringComparer.Ordinal);
        foreach (var item in _methods)
        {
            if (item.Method.DeclaringType?.IsAssignableFrom(componentType) != true) continue;
            var distance = InheritanceDistance(componentType, item.Method.DeclaringType);
            if (!candidates.TryGetValue(item.Name, out var previous) || distance < previous.Distance ||
                distance == previous.Distance && string.Compare(item.Method.Name, previous.Item.Method.Name, StringComparison.Ordinal) < 0)
                candidates[item.Name] = (item, distance);
        }
        var selected = new AttributedMethod[candidates.Count];
        var selectedIndex = 0;
        foreach (var candidate in candidates.Values) selected[selectedIndex++] = candidate.Item;
        Array.Sort(selected, static (left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));

        var commands = new List<ComponentContextMenuCommand>(selected.Length);
        foreach (var item in selected)
        {
            var feature = $"ContextMenu callback {Describe(item.Method)}";
            if (!EditorCallbacks.TryInvoke(feature, () => Compile(item.Method), null, out var callback) ||
                callback is null)
                continue;
            commands.Add(new ComponentContextMenuCommand(item.Name, item.Method.Name, callback));
        }
        cached = commands.ToArray();
        Commands[componentType] = cached;
        return cached;
    }

    private static void EnsureFresh()
    {
        var generation = RuntimeTypeCache.stats.Generation;
        if (_generation == generation) return;
        Commands.Clear();
        var methods = new List<AttributedMethod>();
        {
            var __filteredSource3045 = TypeCache.GetMethodsWithAttribute<ContextMenuAttribute>();
            ArgumentNullException.ThrowIfNull(__filteredSource3045, "source");
            foreach (var method in __filteredSource3045)
            {
                if (!(method.DeclaringType is not null && typeof(Component).IsAssignableFrom(method.DeclaringType)))
                    continue;
                if (!IsValid(method))
                {
                    Debug.LogWarning($"ContextMenu method must be instance void with no parameters: {Describe(method)}");
                    continue;
                }
        
                foreach (var attribute in AttributesFor(method))
                {
                    var name = attribute.itemName.Replace('\\', '/').Trim('/');
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        Debug.LogWarning($"ContextMenu item name cannot be empty: {Describe(method)}");
                        continue;
                    }
        
                    methods.Add(new AttributedMethod(method, name));
                }
            }
        }
        _methods = methods.ToArray();
        _generation = generation;
    }

    private static bool IsValid(MethodInfo method) =>
        !method.IsStatic && !method.ContainsGenericParameters && method.ReturnType == typeof(void) &&
        method.GetParameters().Length == 0;

    private static ContextMenuAttribute[] AttributesFor(MethodInfo method)
    {
        var feature = $"ContextMenu attributes {Describe(method)}";
        return EditorCallbacks.TryInvoke(feature,
            () => method.GetCustomAttributes<ContextMenuAttribute>(inherit: false).ToArray(), [], out var attributes)
            ? attributes : [];
    }

    private static Action<Component> Compile(MethodInfo method)
    {
        var component = Expression.Parameter(typeof(Component), "component");
        var instance = Expression.Convert(component, method.DeclaringType!);
        return Expression.Lambda<Action<Component>>(Expression.Call(instance, method), component).Compile();
    }

    private static int InheritanceDistance(Type componentType, Type declaringType)
    {
        var distance = 0;
        for (var current = componentType; current is not null; current = current.BaseType, distance++)
            if (current == declaringType) return distance;
        return int.MaxValue;
    }

    private static string Describe(MethodInfo method) => $"{method.DeclaringType?.FullName}.{method.Name}";

    private readonly record struct AttributedMethod(MethodInfo Method, string Name);
}
