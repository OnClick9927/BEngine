namespace BEngine.Navigation2D;

[DisallowMultipleComponent]
[AddComponentMenu("Navigation 2D/Navigation Obstacle 2D")]
public sealed class NavigationObstacle2D : Behaviour
{
    public NavigationObstacleShape2D shape { get; set; } = NavigationObstacleShape2D.Box;
    public Vector2 center { get; set; }
    public Vector2 size { get; set; } = Vector2.one;
    public Fix64 radius { get; set; } = Fix64.Half;
    public bool carving { get; set; } = true;
    public bool carveOnlyStationary { get; set; } = true;

    internal bool Contains(Vector2 point, Fix64 padding)
    {
        var local = transform.InverseTransformPoint(point) - center;
        return shape == NavigationObstacleShape2D.Box
            ? Mathf.Abs(local.x) <= size.x / 2 + padding &&
              Mathf.Abs(local.y) <= size.y / 2 + padding
            : local.sqrMagnitude <= (radius + padding) * (radius + padding);
    }
}
