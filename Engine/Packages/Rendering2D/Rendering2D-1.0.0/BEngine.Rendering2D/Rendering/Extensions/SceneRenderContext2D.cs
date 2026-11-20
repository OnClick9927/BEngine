namespace BEngine.Rendering;

/// <summary>Camera collection context shared by render extensions. Valid only during collection.</summary>
[RuntimeTypeAlias("BEngine.Rendering.SceneRenderContext2D, BEngine.Graphics")]
[RuntimeTypeAlias("BEngine.Rendering.SceneRenderContext2D, BEngine.Graphics, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null")]
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
