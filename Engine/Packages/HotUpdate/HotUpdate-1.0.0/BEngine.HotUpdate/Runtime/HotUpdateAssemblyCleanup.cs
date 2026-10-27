using System.Reflection;
using BEngine.Rendering;
using BEngine.Serialization;

namespace BEngine.HotUpdate;

/// <summary>
/// Removes host-owned references to assemblies before their managed-code runtime is released.
/// HotUpdate activation and disposal run on the engine main thread, so registry restoration is deterministic.
/// </summary>
internal static class HotUpdateAssemblyCleanup
{
    internal static void Unregister(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        // Gate/Domain rollback can call this before the runtime releases its
        // lease. Preserve canonical callbacks/types while any stage still owns
        // them; the last runtime releases its use before entering this method.
        var hotAssemblies = ManagedCodePrecompiledAssemblies.GetUnleasedAssemblies(assemblies);
        if (hotAssemblies.Length == 0) return;

        List<Exception>? failures = null;
        TryCleanup(() => BTaskScheduler.Main.RemoveWorkReferencing(hotAssemblies), ref failures);
        foreach (var assembly in hotAssemblies)
        {
            TryCleanup(() => RuntimeAssetCodecRegistry.UnregisterAssembly(assembly), ref failures);
            TryCleanup(() => SpriteSourceRegistry.UnregisterAssembly(assembly), ref failures);
            TryCleanup(() => YamlUtility.UnregisterConverters(assembly), ref failures);
            TryCleanup(() => BEngine.DependencyInjection.RuntimeServiceContributions.UnregisterAssembly(assembly), ref failures);
            TryCleanup(() => SceneRenderContributor2DRegistry.UnregisterAssembly(assembly), ref failures);
            TryCleanup(() => ScenePostProcessor2DRegistry.UnregisterAssembly(assembly), ref failures);
            TryCleanup(() => RuntimeSystemRegistry.UnregisterAssembly(assembly), ref failures);
            TryCleanup(() => SceneAssetSerialization.UnregisterAssembly(assembly), ref failures);
        }

        TryCleanup(() => RemoveHostDelegateReferences(hotAssemblies), ref failures);
        TryCleanup(() => RuntimeTypeCache.UnregisterAssemblies(hotAssemblies), ref failures);
        if (failures is { Count: > 0 })
            throw new AggregateException("HotUpdate assembly registration cleanup failed.", failures);
    }

    private static void RemoveHostDelegateReferences(IReadOnlyCollection<Assembly> hotAssemblies)
    {
        var hotSet = hotAssemblies.ToHashSet();
        var hostTypes = new HashSet<Type>(GetLoadableTypes(typeof(BObject).Assembly));
        foreach (var type in RuntimeTypeCache.GetAllTypes()) hostTypes.Add(type);

        List<Exception>? failures = null;
        foreach (var type in hostTypes)
        {
            if (hotSet.Contains(type.Assembly) || type.ContainsGenericParameters) continue;
            FieldInfo[] fields;
            try
            {
                fields = type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            }
            catch
            {
                continue;
            }

            foreach (var field in fields)
            {
                if (!typeof(Delegate).IsAssignableFrom(field.FieldType)) continue;
                Delegate? current;
                try { current = field.GetValue(null) as Delegate; }
                catch { continue; }
                if (current is null) continue;
                var invocationList = current.GetInvocationList();
                var remaining = invocationList.Where(handler =>
                    !ReferencesAnyAssembly(handler, hotSet)).ToArray();
                if (remaining.Length == invocationList.Length) continue;
                try
                {
                    field.SetValue(null, remaining.Length == 0 ? null : Delegate.Combine(remaining));
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(new InvalidOperationException(
                        $"Could not remove HotUpdate delegate references from '{type.FullName}.{field.Name}'.",
                        exception));
                }
            }
        }

        if (failures is { Count: > 0 })
            throw new AggregateException("HotUpdate static delegate cleanup failed.", failures);
    }

    private static bool ReferencesAnyAssembly(Delegate handler, IReadOnlySet<Assembly> assemblies) =>
        (handler.Method.DeclaringType is { } declaringType && assemblies.Contains(declaringType.Assembly)) ||
        (handler.Target is { } target && assemblies.Contains(target.GetType().Assembly));

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(static type => type is not null).Cast<Type>().ToArray();
        }
    }

    private static void TryCleanup(Action cleanup, ref List<Exception>? failures)
    {
        try { cleanup(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
    }
}
