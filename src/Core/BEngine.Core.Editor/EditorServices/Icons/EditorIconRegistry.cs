using System.Reflection;

namespace BEngine.Editor;

public static class EditorIconRegistry
{
    private static readonly Dictionary<string, List<NameRegistration>> Names = new(StringComparer.OrdinalIgnoreCase);
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void RegisterName(string name, string path)
    {
        var owner = Assembly.GetCallingAssembly();
        lock (Gate)
        {
            if (!Names.TryGetValue(name, out var entries)) Names.Add(name, entries = []);
            entries.RemoveAll(entry => entry.Owner == owner);
            entries.Add(new(path, owner));
        }
    }
    internal static string? ResolveName(string name)
    { lock (Gate) return Names.TryGetValue(name, out var entries) ? entries[^1].Path : null; }
    private static readonly Lock Gate = new();
    private static readonly List<Registration> Registrations = [];
    private static readonly List<Registration> ActiveRegistrations = [];
    private static readonly Dictionary<Type, int> LatestRegistration = [];
    private static readonly Dictionary<Type, string?> Resolved = [];
    private static readonly List<FallbackRegistration> ComponentFallbacks = [];
    private static readonly List<FallbackRegistration> TypeFallbacks = [];

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void SetComponentFallback(Func<Type, string>? fallback)
    { lock (Gate) SetFallback(ComponentFallbacks, fallback, Assembly.GetCallingAssembly()); }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void SetTypeFallback(Func<Type, string?>? fallback)
    { lock (Gate) { SetFallback(TypeFallbacks, fallback, Assembly.GetCallingAssembly()); Resolved.Clear(); } }

    private static void SetFallback(List<FallbackRegistration> entries, Func<Type, string?>? callback, Assembly owner)
    {
        // An explicit null still clears the channel. Assembly retirement, in
        // contrast, only removes that owner's contribution and restores others.
        if (callback is null) { entries.Clear(); return; }
        entries.RemoveAll(entry => entry.Owner == owner);
        entries.Add(new(callback, owner));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void Register(Type type, string resourcePath, bool useForChildren = false)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);
        var owner = Assembly.GetCallingAssembly();
        lock (Gate)
        {
            Registrations.RemoveAll(item => item.Type == type && item.Owner == owner);
            Registrations.Add(new Registration(type, resourcePath, useForChildren, owner));
            RebuildRegistrations();
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static string? GetIconPath(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        lock (Gate)
        {
            return Resolved.TryGetValue(type, out var cached) ? cached : ResolveUncached(type);
        }
    }

    // Keep reflection and registration enumeration off the cached lookup's
    // stack frame. The caller holds Gate throughout both paths.
    private static string? ResolveUncached(Type type)
    {
        Registration? registration = null;
        foreach (var item in ActiveRegistrations)
        {
            if (item.Type == type) { registration = item; break; }
            if (registration is null && item.UseForChildren && item.Type.IsAssignableFrom(type)) registration = item;
        }
        var resourcePath = registration?.ResourcePath ??
                           type.GetCustomAttribute<EditorIconAttribute>(inherit: true)?.resourcePath ??
                           (TypeFallbacks.Count == 0 ? null : TypeFallbacks[^1].Callback(type));
        return Resolved[type] = string.IsNullOrWhiteSpace(resourcePath)
            ? null
            : EditorResource.FindPath(resourcePath) ?? resourcePath;
    }

    public static string GetComponentIconPath(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var registered = GetIconPath(type);
        if (!string.IsNullOrWhiteSpace(registered)) return registered;
        Func<Type, string?>? fallback;
        lock (Gate) fallback = ComponentFallbacks.Count == 0 ? null : ComponentFallbacks[^1].Callback;
        return fallback?.Invoke(type) ?? string.Empty;
    }

    private static void RebuildRegistrations()
    {
        LatestRegistration.Clear();
        for (var index = 0; index < Registrations.Count; index++) LatestRegistration[Registrations[index].Type] = index;
        ActiveRegistrations.Clear();
        for (var index = 0; index < Registrations.Count; index++)
            if (LatestRegistration[Registrations[index].Type] == index) ActiveRegistrations.Add(Registrations[index]);
        Resolved.Clear();
    }

    internal static void UnregisterAssembly(System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (Gate)
        {
            Registrations.RemoveAll(item => item.Type.Assembly == assembly || item.Owner == assembly);
            ComponentFallbacks.RemoveAll(entry => entry.Owner == assembly || entry.Callback.Method.Module.Assembly == assembly);
            TypeFallbacks.RemoveAll(entry => entry.Owner == assembly || entry.Callback.Method.Module.Assembly == assembly);
            foreach (var entry in Names.ToArray())
            {
                entry.Value.RemoveAll(item => item.Owner == assembly);
                if (entry.Value.Count == 0) Names.Remove(entry.Key);
            }
            RebuildRegistrations();
        }
    }

    private readonly record struct Registration(Type Type, string ResourcePath, bool UseForChildren, Assembly Owner);
    private readonly record struct NameRegistration(string Path, Assembly Owner);
    private readonly record struct FallbackRegistration(Func<Type, string?> Callback, Assembly Owner);
}
