namespace BEngine;

[AddComponentMenu("Rendering/2D Shadow Caster")]
public sealed class ShadowCaster2D : Behaviour
{
    public List<Vector2> shapePath { get; set; } =
    [
        new(-Fix64.Half, -Fix64.Half),
        new(Fix64.Half, -Fix64.Half),
        new(Fix64.Half, Fix64.Half),
        new(-Fix64.Half, Fix64.Half)
    ];

    public bool castsShadows { get; set; } = true;
    public LayerMask layerMask { get; set; } = new(ulong.MaxValue);

    internal void FillWorldPath(List<Vector2> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        foreach (var point in shapePath) destination.Add(transform.TransformPoint(point));
    }
}
