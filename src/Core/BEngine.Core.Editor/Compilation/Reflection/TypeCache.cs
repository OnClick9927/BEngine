using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Serialization;

namespace BEngine.Editor;

public static class TypeCache
{
    private static readonly Dictionary<string,Action> Warmups = new(StringComparer.Ordinal);
    public static void RegisterWarmup(string name, Action callback) => Warmups[name] = callback;
    public static void UnregisterWarmups(Assembly owner)
    {
        foreach(var name in Warmups.Where(pair=>pair.Value.Method.DeclaringType?.Assembly==owner).Select(pair=>pair.Key).ToArray()) Warmups.Remove(name);
    }

    internal static void ForgetAssembly(Assembly owner)
    {
        UnregisterWarmups(owner);
        lock (Gate)
        {
            RetiredAssemblies.GetValue(owner, static _ => new object());
            // Cached results can hold a retiring type as either key or value.
            // Invalidate without invoking surviving extension callbacks during unload.
            Derived.Clear(); AttributedTypes.Clear(); AttributedMethods.Clear();
            _generation = -1;
        }
    }

    private static readonly Lock Gate = new();
    private static readonly Dictionary<Type, Type[]> Derived = [];
    private static readonly Dictionary<Type, Type[]> AttributedTypes = [];
    private static readonly Dictionary<Type, MethodInfo[]> AttributedMethods = [];
    // Cache immutable reference metadata once per assembly instead of once per type/query.
    // Weak keys allow collectible package assemblies to unload with their cached metadata.
    private static readonly ConditionalWeakTable<Assembly, AssemblyReferenceNames> ReferenceNames = new();
    private static readonly ConditionalWeakTable<Assembly, object> RetiredAssemblies = new();
    private static int _generation = -1;

    internal static void Warmup()
    {
        EditorFeatureGuard.Invoke("TypeCache.RuntimeTypeCache.Warmup", RuntimeTypeCache.Warmup);
        EditorFeatureGuard.Invoke("TypeCache.Refresh", Refresh);
    }

    internal static void Refresh()
    {
        EnsureFresh();
        Warm("InitializeOnLoad", () => _ = GetTypesWithAttribute<InitializeOnLoadAttribute>());
        Warm("CreateAssetMenu", () => _ = GetTypesWithAttribute<CreateAssetMenuAttribute>());
        Warm("MenuItems", () => _ = GetMethodsWithAttribute<MenuItemAttribute>());
        Warm("SettingsProviders", () => _ = GetMethodsWithAttribute<SettingsProviderAttribute>());
        Warm("PreferenceItems", () => _ = GetMethodsWithAttribute<PreferenceItemAttribute>());
        Warm("InitializeOnLoadMethods", () => _ = GetMethodsWithAttribute<InitializeOnLoadMethodAttribute>());
        Warm("DidReloadScripts", () => _ = GetMethodsWithAttribute<DidReloadScriptsAttribute>());
        Warm("ContextMenus", () => _ = GetMethodsWithAttribute<ContextMenuAttribute>());
        Warm("DrawGizmos", () => _ = GetMethodsWithAttribute<DrawGizmoAttribute>());
        Warm("OnOpenAsset", () => _ = GetMethodsWithAttribute<OnOpenAssetAttribute>());
        Warm("SettingsProviderRegistry", SettingsProviderRegistry.Warmup);
        foreach (var (name, callback) in Warmups.ToArray()) Warm(name, callback);
    }

    public static Type[] GetTypesDerivedFrom<T>() => GetTypesDerivedFrom(typeof(T));
    public static Type[] GetTypesDerivedFrom(Type parentType)
    {
        ArgumentNullException.ThrowIfNull(parentType);
        lock (Gate)
        {
            EnsureFresh();
            if (Derived.TryGetValue(parentType, out var cached)) return cached;
            cached = RuntimeTypeCache.GetTypesDerivedFrom(parentType)
                .Where(type => !RetiredAssemblies.TryGetValue(type.Assembly, out _)).ToArray();
            Derived[parentType] = cached;
            return cached;
        }
    }

    public static Type[] GetTypesWithAttribute<T>() where T : Attribute => GetTypesWithAttribute(typeof(T));
    public static Type[] GetTypesWithAttribute(Type attributeType)
    {
        ArgumentNullException.ThrowIfNull(attributeType);
        lock (Gate)
        {
            EnsureFresh();
            if (AttributedTypes.TryGetValue(attributeType, out var cached)) return cached;
            cached = ReferencingTypes(attributeType.Assembly)
                .Where(type => HasAttribute(type, attributeType)).ToArray();
            AttributedTypes[attributeType] = cached;
            return cached;
        }
    }

    public static MethodInfo[] GetMethodsWithAttribute<T>() where T : Attribute =>
        GetMethodsWithAttribute(typeof(T));
    public static MethodInfo[] GetMethodsWithAttribute(Type attributeType)
    {
        ArgumentNullException.ThrowIfNull(attributeType);
        lock (Gate)
        {
            EnsureFresh();
            if (AttributedMethods.TryGetValue(attributeType, out var cached)) return cached;
            cached = ReferencingTypes(attributeType.Assembly)
                .SelectMany(RuntimeTypeCache.GetMethods).Where(method => HasAttribute(method, attributeType)).ToArray();
            AttributedMethods[attributeType] = cached;
            return cached;
        }
    }

    internal static Type[] GetAllTypes() => RuntimeTypeCache.GetAllTypes();

    private static void EnsureFresh()
    {
        var generation = RuntimeTypeCache.stats.Generation;
        if (_generation == generation) return;
        Derived.Clear(); AttributedTypes.Clear(); AttributedMethods.Clear();
        _generation = generation;
    }

    private static bool HasAttribute(MemberInfo member, Type attributeType)
    {
        try { return member.IsDefined(attributeType, inherit: false); }
        catch { return false; }
    }

    private static IEnumerable<Type> ReferencingTypes(Assembly contract)
    {
        // A query needs the contract name once, not a new AssemblyName for every type.
        // Adjacent types normally share an assembly; preserve their discovery order.
        var contractName = contract.GetName().Name;
        Assembly? previous = null;
        var included = false;
        foreach (var type in RuntimeTypeCache.GetAllTypes())
        {
            var candidate = type.Assembly;
            if (!ReferenceEquals(previous, candidate))
            {
                included = !RetiredAssemblies.TryGetValue(candidate, out _) &&
                    (ReferenceEquals(candidate, contract) || References(candidate, contractName));
                previous = candidate;
            }
            if (included) yield return type;
        }
    }

    private static bool References(Assembly candidate, string? contractName)
    {
        try
        {
            var references = ReferenceNames.GetValue(candidate, static assembly =>
                new AssemblyReferenceNames(assembly.GetReferencedAssemblies()
                    .Select(reference => reference.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!)
                    .ToHashSet(StringComparer.Ordinal)));
            return contractName is not null && references.Names.Contains(contractName);
        }
        catch
        {
            return false;
        }
    }

    private sealed class AssemblyReferenceNames(HashSet<string> names)
    {
        internal HashSet<string> Names { get; } = names;
    }

    private static void Warm(string name, Action action) =>
        EditorFeatureGuard.Invoke($"TypeCache.{name}.Warmup", action);
}
