using System.Reflection;

namespace BEngine.Editor;

internal static class EditorTypeRegistry
{
    private static readonly object Gate = new();
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
            cached = _registrations.FirstOrDefault(item =>
                item.InspectedType == inspectedType || item.EditorForChildClasses &&
                item.InspectedType.IsAssignableFrom(inspectedType)).EditorType;
            Resolved[inspectedType] = cached;
            return cached;
        }
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
        return EditorFeatureGuard.TryInvoke(feature,
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
