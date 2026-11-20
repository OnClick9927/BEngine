using System.Reflection;

namespace BEngine.Serialization;

/// <summary>A read-only view of a component's legacy serialized reference fields.</summary>
public readonly struct SceneComponentReferences
{
    private readonly IReadOnlyDictionary<string, string>? _fields;
    internal SceneComponentReferences(IReadOnlyDictionary<string, string> fields) => _fields = fields;
    public bool TryGetValue(string name, out string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_fields is null) throw new InvalidOperationException("Component references are uninitialized.");
        return _fields.TryGetValue(name, out value!);
    }
}

/// <summary>Component-owned reference migration after ordinary scene hydration.
/// Runs only when the caller supplies a resource object provider; it never runs per frame.</summary>
public abstract class SceneReferenceRestorer
{
    public abstract Type ComponentType { get; }
    public abstract void Restore(Component component, SceneComponentReferences references,
        IResourceObjectProvider resources);
}

/// <summary>Engine-thread registrations owned by the implementing assembly.</summary>
public static class SceneReferenceRestorers
{
    private sealed record Entry(Type ComponentType, SceneReferenceRestorer Restorer, Assembly Owner);
    private static readonly List<Entry> Entries = [];
    private static readonly Dictionary<Type, SceneReferenceRestorer[]> Cache = [];
    static SceneReferenceRestorers() => RuntimeAssemblyCleanup.Register(UnregisterAssembly);

    public static void Register(SceneReferenceRestorer restorer)
    {
        ArgumentNullException.ThrowIfNull(restorer);
        var type = restorer.ComponentType;
        if (type is null || type.ContainsGenericParameters || !typeof(Component).IsAssignableFrom(type))
            throw new ArgumentException("A closed component type is required.", nameof(restorer));
        Entries.RemoveAll(entry => entry.Restorer.GetType() == restorer.GetType());
        Entries.Add(new(type, restorer, restorer.GetType().Assembly));
        Cache.Clear();
    }

    public static void UnregisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        // Cached keys can themselves be types from the departing module.
        Entries.RemoveAll(entry => entry.Owner == assembly || entry.ComponentType.Assembly == assembly);
        Cache.Clear();
    }

    internal static void Restore(Component component, IReadOnlyDictionary<string, string> fields,
        IResourceObjectProvider resources)
    {
        var type = component.GetType();
        if (!Cache.TryGetValue(type, out var restorers))
        {
            restorers = Entries.Where(entry => entry.ComponentType.IsAssignableFrom(type))
                .Select(entry => entry.Restorer).ToArray();
            Cache.Add(type, restorers);
        }
        foreach (var restorer in restorers)
            restorer.Restore(component, new(fields), resources);
    }
}
