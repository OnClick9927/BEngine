using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Serialization;

namespace BEngine.Editor;

public static class TypeCache
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Type, Type[]> Derived = [];
    private static readonly Dictionary<Type, Type[]> AttributedTypes = [];
    private static readonly Dictionary<Type, MethodInfo[]> AttributedMethods = [];
    private static int _generation = -1;

    internal static void Warmup()
    {
        EditorFeatureGuard.Invoke("TypeCache.RuntimeTypeCache.Warmup", RuntimeTypeCache.Warmup);
        EditorFeatureGuard.Invoke("TypeCache.Refresh", Refresh);
    }

    internal static void Refresh()
    {
        EnsureFresh();
        Warm("Editors", () => _ = GetTypesDerivedFrom<Editor>());
        Warm("PropertyDrawers", () => _ = GetTypesDerivedFrom<PropertyDrawer>());
        Warm("AssetPostprocessors", () => _ = GetTypesDerivedFrom<AssetPostprocessor>());
        Warm("AssetModificationProcessors", () => _ = GetTypesDerivedFrom<AssetModificationProcessor>());
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
        Warm("PropertyDrawerRegistry", PropertyDrawerRegistry.Warmup);
        Warm("EditorTypeRegistry", EditorTypeRegistry.Warmup);
        Warm("SerializedMemberMetadata", SerializedMemberMetadata.Warmup);
        Warm("EditorWindowMetadataRegistry", EditorWindowMetadataRegistry.Warmup);
        Warm("EditorReflectionCache", EditorReflectionCache.Warmup);
        Warm("AssetProcessorRegistry", AssetProcessorRegistry.Warmup);
        Warm("SettingsProviderRegistry", SettingsProviderRegistry.Warmup);
        Warm("CreateAssetMenuRegistry", CreateAssetMenuRegistry.Warmup);
    }

    public static Type[] GetTypesDerivedFrom<T>() => GetTypesDerivedFrom(typeof(T));
    public static Type[] GetTypesDerivedFrom(Type parentType)
    {
        ArgumentNullException.ThrowIfNull(parentType);
        lock (Gate)
        {
            EnsureFresh();
            if (Derived.TryGetValue(parentType, out var cached)) return cached;
            cached = RuntimeTypeCache.GetTypesDerivedFrom(parentType);
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
            cached = RuntimeTypeCache.GetAllTypes().Where(type => References(type.Assembly, attributeType.Assembly))
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
            cached = RuntimeTypeCache.GetAllTypes().Where(type => References(type.Assembly, attributeType.Assembly))
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

    private static bool References(Assembly candidate, Assembly contract)
    {
        if (ReferenceEquals(candidate, contract)) return true;
        var contractName = contract.GetName().Name;
        try
        {
            return candidate.GetReferencedAssemblies().Any(reference =>
                string.Equals(reference.Name, contractName, StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
    }

    private static void Warm(string name, Action action) =>
        EditorFeatureGuard.Invoke($"TypeCache.{name}.Warmup", action);
}
