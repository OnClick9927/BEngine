namespace BEngine;

public static class HierarchyOrder2D
{
    public static IReadOnlyDictionary<GameObject, long> Build(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var result = new Dictionary<GameObject, long>(ReferenceEqualityComparer.Instance);
        Fill(scene, result);
        return result;
    }

    internal static void Fill(Scene scene, Dictionary<GameObject, long> result)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(result);
        result.Clear();
        long order = 0;
        foreach (var gameObject in scene.GameObjectsSpanUnchecked)
            if (gameObject.TransformUnchecked.ParentUnchecked is null)
                Add(gameObject.TransformUnchecked, result, ref order);
    }

    private static void Add(Transform transform, IDictionary<GameObject, long> result, ref long order)
    {
        result[transform.gameObject] = order++;
        foreach (var child in transform.children) Add(child, result, ref order);
    }
}
