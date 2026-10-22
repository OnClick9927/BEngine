namespace BEngine.Rendering;

/// <summary>Camera collection context shared by render extensions. Valid only during collection.</summary>
public readonly record struct SceneRenderContext2D(
    Scene Scene,
    RenderCamera Camera,
    int Width,
    int Height,
    IReadOnlyDictionary<GameObject, long> HierarchyOrder,
    Predicate<GameObject>? ObjectFilter = null)
{
    public long GetHierarchyOrder(GameObject gameObject) => HierarchyOrder.GetValueOrDefault(gameObject);
}
