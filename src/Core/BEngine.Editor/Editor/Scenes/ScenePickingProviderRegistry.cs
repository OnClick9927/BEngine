using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Rendering;

namespace BEngine.Editor;

public readonly record struct ScenePickCandidate(GameObject GameObject, RenderSortKey2D SortKey);

public delegate long ScenePickingProvider(
    Scene scene,
    RenderCamera camera,
    Vector2 viewportPoint,
    int viewportWidth,
    int viewportHeight,
    ICollection<ScenePickCandidate> candidates,
    long submissionOrder,
    Predicate<GameObject>? objectFilter);

/// <summary>Registry for package-defined Scene view picking contributors.</summary>
public static class ScenePickingProviderRegistry
{
    private static readonly Dictionary<string, Registration> Providers = new(StringComparer.Ordinal);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Register(string id, ScenePickingProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(provider);
        Providers[id] = new Registration(provider, Assembly.GetCallingAssembly());
    }

    public static bool Unregister(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Providers.Remove(id);
    }

    internal static void UnregisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var id in Providers.Where(pair =>
                     ReferenceEquals(pair.Value.Owner, assembly) ||
                     ReferenceEquals(pair.Value.Provider.Method.DeclaringType?.Assembly, assembly))
                 .Select(pair => pair.Key).ToArray())
            Providers.Remove(id);
    }

    internal static long Collect(
        Scene scene,
        RenderCamera camera,
        Vector2 viewportPoint,
        int viewportWidth,
        int viewportHeight,
        ICollection<ScenePickCandidate> candidates,
        long submissionOrder,
        Predicate<GameObject>? objectFilter)
    {
        foreach (var (id, registration) in Providers.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var isolated = new List<ScenePickCandidate>();
            if (!EditorFeatureGuard.TryInvoke($"Scene picking provider {id}",
                    () => registration.Provider(scene, camera, viewportPoint,
                        viewportWidth, viewportHeight, isolated, submissionOrder, objectFilter),
                    submissionOrder, out var nextSubmissionOrder))
                continue;

            submissionOrder = Math.Max(submissionOrder, nextSubmissionOrder);
            foreach (var candidate in isolated)
            {
                if (candidate.GameObject is null ||
                    !ReferenceEquals(candidate.GameObject.scene, scene) ||
                    objectFilter is not null && !objectFilter(candidate.GameObject)) continue;
                candidates.Add(candidate);
            }
        }
        return submissionOrder;
    }

    private readonly record struct Registration(ScenePickingProvider Provider, Assembly Owner);
}
