using System.Reflection;

namespace BEngine.Editor;

/// <summary>Captures additional in-memory authoring data owned by an object's feature module.
/// Implementations run on the editor thread. Payloads must be reusable, owned managed data;
/// do not capture native handles, services, or mutable buffers borrowed from the target.</summary>
public abstract class EditorObjectSnapshotAdapter
{
    public abstract Type ObjectType { get; }
    public abstract object? Capture(BObject target);
    public abstract void Restore(BObject target, object? payload);
}

/// <summary>Optional contributions to Core's object snapshots, including Undo and value copying.
/// Registrations and lookup caches are retired with their implementing or target assembly.
/// Caller-owned snapshots must be released before unloading their target objects.</summary>
public static class EditorObjectSnapshotAdapters
{
    private static readonly List<Registration> Entries = [];
    private static readonly Dictionary<Type, Registration[]> Cache = [];

    static EditorObjectSnapshotAdapters() => RuntimeAssemblyCleanup.Register(UnregisterAssembly);

    public static void Register(EditorObjectSnapshotAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var type = adapter.ObjectType;
        if (type is null || type.ContainsGenericParameters || !typeof(BObject).IsAssignableFrom(type))
            throw new ArgumentException("A closed engine object type is required.", nameof(adapter));
        for (var i = Entries.Count - 1; i >= 0; i--)
        {
            if (Entries[i].Adapter!.GetType() != adapter.GetType()) continue;
            Entries[i].Retire();
            Entries.RemoveAt(i);
        }
        Entries.Add(new(adapter, type));
        Cache.Clear();
    }

    public static void UnregisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        for (var i = Entries.Count - 1; i >= 0; i--)
        {
            var entry = Entries[i];
            if (!ReferencesAssembly(entry.Adapter!.GetType(), assembly) && !ReferencesAssembly(entry.ObjectType!, assembly)) continue;
            entry.Retire();
            Entries.RemoveAt(i);
        }
        Cache.Clear();
    }

    private static bool ReferencesAssembly(Type type, Assembly assembly)
    {
        if (type.Assembly == assembly) return true;
        if (type.HasElementType && ReferencesAssembly(type.GetElementType()!, assembly)) return true;
        if (type.IsGenericType)
            foreach (var argument in type.GetGenericArguments())
                if (ReferencesAssembly(argument, assembly)) return true;
        return false;
    }

    internal static Captured[] Capture(BObject target)
    {
        var type = target.GetType();
        if (!Cache.TryGetValue(type, out var entries))
        {
            List<Registration>? matches = null;
            foreach (var entry in Entries)
                if (entry.ObjectType!.IsAssignableFrom(type)) (matches ??= []).Add(entry);
            entries = matches?.ToArray() ?? [];
            Cache.Add(type, entries);
        }
        if (entries.Length == 0) return [];
        var result = new Captured[entries.Length];
        for (var i = 0; i < entries.Length; i++)
            result[i] = new(entries[i], entries[i].Adapter!.Capture(target));
        return result;
    }

    // A snapshot holds this revocable registration rather than a provider delegate. Retirement
    // releases the adapter and target Type even when a caller still retains an old snapshot.
    internal sealed class Registration(EditorObjectSnapshotAdapter adapter, Type objectType)
    {
        internal EditorObjectSnapshotAdapter? Adapter { get; private set; } = adapter;
        internal Type? ObjectType { get; private set; } = objectType;
        internal void Retire() { Adapter = null; ObjectType = null; }
    }

    internal readonly struct Captured(Registration registration, object? payload)
    {
        internal long EstimatedBytes => EditorStateMemoryEstimate.Measure(payload);
        internal void Validate() => ObjectDisposedException.ThrowIf(registration.Adapter is null, nameof(EditorObjectSnapshotAdapter));
        internal void Restore(BObject target)
        {
            Validate();
            registration.Adapter!.Restore(target, payload);
        }
    }
}
