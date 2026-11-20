using System.Reflection;

namespace BEngine.Editor;

internal static class EditorTypeRegistry
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<Type, Type?> Resolved = [];
    private static Registration[] _registrations = [];
    private static int _generation = -1;

    internal static void Warmup()
    {
        lock (Gate) EnsureFresh();
    }

    internal static Type? Find(Type inspectedType)
    {
        lock (Gate)
        {
            EnsureFresh();
            if (Resolved.TryGetValue(inspectedType, out var cached)) return cached;
            Type? best = null;
            var bestExact = false;
            var bestDistance = int.MaxValue;
            foreach (var item in _registrations)
            {
                var exact = item.InspectedType == inspectedType;
                if (!exact && (!item.EditorForChildClasses || !item.InspectedType.IsAssignableFrom(inspectedType))) continue;
                var distance = InheritanceDistance(inspectedType, item.InspectedType);
                if (best is not null &&
                    (bestExact && !exact || bestExact == exact &&
                        (distance > bestDistance || distance == bestDistance &&
                            StringComparer.Ordinal.Compare(item.EditorType.FullName, best.FullName) >= 0))) continue;
                best = item.EditorType;
                bestExact = exact;
                bestDistance = distance;
            }
            cached = best;
            Resolved[inspectedType] = cached;
            return cached;
        }
    }

    private static int InheritanceDistance(Type concreteType, Type registeredType)
    {
        if (concreteType == registeredType) return 0;
        if (registeredType.IsInterface) return concreteType.GetInterfaces().Contains(registeredType) ? 1 : int.MaxValue;
        var distance = 0;
        for (var current = concreteType; current is not null; current = current.BaseType)
        {
            if (current == registeredType) return distance;
            distance++;
        }
        return int.MaxValue;
    }

    private static void EnsureFresh()
    {
        var generation = RuntimeTypeCache.stats.Generation;
        if (_generation == generation) return;
        _registrations = TypeCache.GetTypesDerivedFrom<Editor>()
            .Where(type => !type.IsAbstract)
            .SelectMany(RegistrationsFor)
            .OrderBy(item => item.EditorForChildClasses)
            .ThenBy(item => item.EditorType.FullName, StringComparer.Ordinal)
            .ToArray();
        Resolved.Clear();
        _generation = generation;
    }

    private static Registration[] RegistrationsFor(Type type)
    {
        var feature = $"CustomEditor attributes {type.FullName}";
        return EditorCallbacks.TryInvoke(feature,
            () => type.GetCustomAttributes<CustomEditorAttribute>(false)
                .Select(attribute => new Registration(type, attribute.inspectedType,
                    attribute.editorForChildClasses)).ToArray(), [], out var registrations)
            ? registrations : [];
    }

    private readonly record struct Registration(
        Type EditorType,
        Type InspectedType,
        bool EditorForChildClasses);
}
