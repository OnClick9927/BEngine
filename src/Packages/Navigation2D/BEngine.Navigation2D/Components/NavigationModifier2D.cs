using BEngine.Physics2D;

namespace BEngine.Navigation2D;

[AddComponentMenu("Navigation 2D/Navigation Modifier 2D")]
public sealed class NavigationModifier2D : Behaviour
{
    public bool overrideArea { get; set; }
    public int area { get; set; }
    public bool affectedByAgentType { get; set; }
    public int agentTypeId { get; set; }

    internal bool Blocks(Vector2 point, int surfaceAgentType)
    {
        if (!enabled || !overrideArea || area != 1 ||
            affectedByAgentType && agentTypeId != surfaceAgentType)
            return false;
        if (GetComponent<Collider2D>() is { } collider) return collider.bounds.Contains(point);
        var local = transform.InverseTransformPoint(point);
        return Mathf.Abs(local.x) <= Fix64.Half && Mathf.Abs(local.y) <= Fix64.Half;
    }
}
