using System.Reflection;
using BEngine.Rendering;
using BEngine.Serialization;

namespace BEngine;

/// <summary>Releases engine-owned registrations and callbacks before an owner unloads its code assemblies.</summary>
public static class RuntimeAssemblyCleanup
{
    private static readonly Dictionary<Assembly, List<Action<Assembly>>> Extensions = [];

    /// <summary>Registers package cleanup without introducing dependencies on package registries.</summary>
    public static void Register(Action<Assembly> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        var owner = cleanup.Method.DeclaringType!.Assembly;
        if (!Extensions.TryGetValue(owner, out var callbacks)) Extensions.Add(owner, callbacks = []);
        if (!callbacks.Contains(cleanup)) callbacks.Add(cleanup);
    }

    public static void UnregisterExtensions(Assembly assembly)
    {
        List<Exception>? failures = null;
        foreach (var callbacks in Extensions.Values.ToArray())
            foreach (var callback in callbacks.ToArray()) TryCleanup(() => callback(assembly), ref failures);
        Extensions.Remove(assembly);
        RuntimeObjectCodecs.UnregisterAssembly(assembly);
        if (failures is { Count: > 0 }) throw new AggregateException("Module cleanup failed.", failures);
    }
    /// <summary>
    /// Unregisters these assemblies from runtime registries, initialization state, frame work and static
    /// delegate fields in indexed host types. Call on the owning engine thread after stopping the domain.
    /// The caller is responsible for assembly leases and for external instance/resource references.
    /// </summary>
    public static void Unregister(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var selected = (((global::System.Collections.Generic.IEnumerable<global::System.Reflection.Assembly>)(assemblies)) switch
        {
            global::System.Reflection.Assembly[] { Length: 0 } => global::System.Array.Empty<global::System.Reflection.Assembly>(),
            var distinctItems1808 => distinctItems1808.ToHashSet<global::System.Reflection.Assembly>().ToArray()});
        if (selected.Length == 0) return;
        var owners = selected.ToHashSet();
        List<Exception>? failures = null;
        TryCleanup(() => CooperativeWorkScheduler.RemoveAllWorkReferencing(selected), ref failures);
        TryCleanup(() => BTaskScheduler.RemoveAllWorkReferencing(selected), ref failures);
        foreach (var assembly in selected)
        {
            TryCleanup(() => RuntimeAssetCodecRegistry.UnregisterAssembly(assembly), ref failures);
            TryCleanup(() => UnregisterExtensions(assembly), ref failures);
            TryCleanup(() => YamlUtility.UnregisterConverters(assembly), ref failures);
            TryCleanup(() => DependencyInjection.RuntimeServiceContributions.UnregisterAssembly(assembly), ref failures);
            TryCleanup(() => RuntimeSystemRegistry.UnregisterAssembly(assembly), ref failures);
            TryCleanup(() => SceneAssetSerialization.UnregisterAssembly(assembly), ref failures);
        }
        TryCleanup(() => RemoveHostDelegateReferences(owners), ref failures);
        TryCleanup(() => RuntimeLifecycle.ForgetAssemblies(owners), ref failures);
        TryCleanup(() => RuntimeTypeCache.UnregisterAssemblies(selected), ref failures);
        if (failures is { Count: > 0 })
            throw new AggregateException("Runtime assembly registration cleanup failed.", failures);
    }

    private static void RemoveHostDelegateReferences(IReadOnlySet<Assembly> assemblies)
    {
        var hostTypes = new HashSet<Type>(GetLoadableTypes(typeof(BObject).Assembly));
        foreach (var type in RuntimeTypeCache.GetAllTypes()) hostTypes.Add(type);
        List<Exception>? failures = null;
        foreach (var type in hostTypes)
        {
            if (assemblies.Contains(type.Assembly) || type.ContainsGenericParameters) continue;
            FieldInfo[] fields;
            try { fields = type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public); }
            catch { continue; }
            foreach (var field in fields)
            {
                if (!typeof(Delegate).IsAssignableFrom(field.FieldType)) continue;
                Delegate? current;
                try { current = field.GetValue(null) as Delegate; }
                catch { continue; }
                if (current is null) continue;
                var invocationList = current.GetInvocationList();
                var remaining = invocationList.Where(handler =>
                    !BTaskScheduler.CallbackReferencesAnyAssembly(handler, assemblies)).ToArray();
                if (remaining.Length == invocationList.Length) continue;
                try { field.SetValue(null, remaining.Length == 0 ? null : Delegate.Combine(remaining)); }
                catch (Exception exception)
                {
                    (failures ??= []).Add(new InvalidOperationException(
                        $"Could not remove runtime delegate references from '{type.FullName}.{field.Name}'.", exception));
                }
            }
        }
        if (failures is { Count: > 0 })
            throw new AggregateException("Runtime static delegate cleanup failed.", failures);
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception)
        { return exception.Types.Where(static type => type is not null).Cast<Type>().ToArray(); }
    }

    private static void TryCleanup(Action cleanup, ref List<Exception>? failures)
    {
        try { cleanup(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
    }
}
