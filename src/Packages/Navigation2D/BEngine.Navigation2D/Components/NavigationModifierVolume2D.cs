namespace BEngine.Navigation2D;

[AddComponentMenu("Navigation 2D/Navigation Modifier Volume 2D")]
public sealed class NavigationModifierVolume2D : Behaviour
{
    public Vector2 center { get; set; }
    public Vector2 size { get; set; } = Vector2.one;
    public int area { get; set; } = 1;

    internal bool Contains(Vector2 point)
    {
        var local = transform.InverseTransformPoint(point) - center;
        return Mathf.Abs(local.x) <= size.x / 2 && Mathf.Abs(local.y) <= size.y / 2;
    }
}
