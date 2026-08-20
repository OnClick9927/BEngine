namespace BEngine;

public static class HierarchyOrder2D
{
    public static IReadOnlyDictionary<GameObject, long> Build(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var result = new Dictionary<GameObject, long>(ReferenceEqualityComparer.Instance);
        long order = 0;
        foreach (var root in scene.rootGameObjects)
            Add(root.transform, result, ref order);
        return result;
    }

    private static void Add(Transform transform, IDictionary<GameObject, long> result, ref long order)
    {
        result[transform.gameObject] = order++;
        foreach (var child in transform.children) Add(child, result, ref order);
    }
}
