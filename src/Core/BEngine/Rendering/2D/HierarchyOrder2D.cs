using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;

namespace BEngine;

public static class HierarchyOrder2D
{
    private static readonly ConditionalWeakTable<Scene, CachedOrder> Cache = new();
    internal static void Forget(Scene scene) => Cache.Remove(scene);
    private sealed class CachedOrder
    {
        internal long Revision = -1;
        internal readonly Dictionary<GameObject, long> Values = new(ReferenceEqualityComparer.Instance);
        internal readonly List<Transform> Traversal = [];
        internal readonly ReadOnlyDictionary<GameObject, long> View;
        internal CachedOrder() => View = new(Values);
    }
    /// <summary>Shared order valid during collection. Read again after hierarchy changes.</summary>
    public static IReadOnlyDictionary<GameObject, long> Get(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var cached = Cache.GetValue(scene, static _ => new CachedOrder());
        if (cached.Revision != scene.HierarchyRevision)
        {
            Fill(scene, cached.Values, cached.Traversal); cached.Revision = scene.HierarchyRevision;
        }
        return cached.View;
    }
    public static IReadOnlyDictionary<GameObject, long> Build(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var result = new Dictionary<GameObject, long>(ReferenceEqualityComparer.Instance);
        Fill(scene, result);
        return result;
    }

    /// <summary>Fills reusable storage in hierarchy order without recursive traversal.</summary>
    public static void Fill(Scene scene, Dictionary<GameObject, long> result)
        => Fill(scene, result, new List<Transform>());

    public static void Fill(Scene scene, Dictionary<GameObject, long> result, List<Transform> traversal)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(traversal);
        result.Clear();
        traversal.Clear();
        long order = 0;
        foreach (var gameObject in scene.GameObjectsSpanUnchecked)
            if (gameObject.TransformUnchecked.ParentUnchecked is null)
            {
                traversal.Add(gameObject.TransformUnchecked);
                while (traversal.Count > 0)
                {
                    var last = traversal.Count - 1;
                    var transform = traversal[last]; traversal.RemoveAt(last);
                    result[transform.gameObject] = order++;
                    var children = transform.ChildrenUnchecked;
                    for (var index = children.Count - 1; index >= 0; index--) traversal.Add(children[index]);
                }
            }
    }
}
