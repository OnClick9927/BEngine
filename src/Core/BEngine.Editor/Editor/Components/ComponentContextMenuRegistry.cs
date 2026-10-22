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
            foreach (var type in TypeCache.GetTypesDerivedFrom<Component>().Where(type => !type.IsAbstract))
                _ = GetCommandsCore(type);
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
        var selected = _methods
            .Where(item => item.Method.DeclaringType?.IsAssignableFrom(componentType) == true)
            .Select(item => (Item: item, Distance: InheritanceDistance(componentType, item.Method.DeclaringType!)))
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Item.Method.Name, StringComparer.Ordinal)
            .GroupBy(item => item.Item.Name, StringComparer.Ordinal)
            .Select(group => group.First().Item)
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();

        var commands = new List<ComponentContextMenuCommand>(selected.Length);
        foreach (var item in selected)
        {
            var feature = $"ContextMenu callback {Describe(item.Method)}";
            if (!EditorFeatureGuard.TryInvoke(feature, () => Compile(item.Method), null, out var callback) ||
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
        foreach (var method in TypeCache.GetMethodsWithAttribute<ContextMenuAttribute>()
                     .Where(method => method.DeclaringType is not null &&
                                      typeof(Component).IsAssignableFrom(method.DeclaringType)))
        {
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
        _methods = methods.ToArray();
        _generation = generation;
    }

    private static bool IsValid(MethodInfo method) =>
        !method.IsStatic && !method.ContainsGenericParameters && method.ReturnType == typeof(void) &&
        method.GetParameters().Length == 0;

    private static ContextMenuAttribute[] AttributesFor(MethodInfo method)
    {
        var feature = $"ContextMenu attributes {Describe(method)}";
        return EditorFeatureGuard.TryInvoke(feature,
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
