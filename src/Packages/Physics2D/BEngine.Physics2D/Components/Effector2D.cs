namespace BEngine.Physics2D;

public abstract class Effector2D : Component
{
    public ulong colliderMask { get; set; } = ulong.MaxValue;
    internal bool Affects(Collider2D other) => (colliderMask & SortingLayer.ToMask(other.gameObject.layer)) != 0;
}

[AddComponentMenu("Physics 2D/Platform Effector 2D")]
public sealed class PlatformEffector2D : Effector2D
{
    public Fix64 surfaceArc { get; set; } = 180;
    public Fix64 rotationalOffset { get; set; }
    internal bool Allows(Vector2 ownToOtherNormal, Collider2D other)
    {
        if (!enabled || !Affects(other)) return true;
        var radians = (transform.rotation + rotationalOffset) * Mathf.Deg2Rad;
        var up = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians));
        return Vector2.Dot(ownToOtherNormal, up) >= Mathf.Cos(Mathf.Clamp(surfaceArc, 0, 360) * Mathf.Deg2Rad * Fix64.Half);
    }
}

[AddComponentMenu("Physics 2D/Area Effector 2D")]
public sealed class AreaEffector2D : Effector2D
{
    public Fix64 forceAngle { get; set; } = 90;
    public Fix64 forceMagnitude { get; set; } = 10;
    internal Vector2 Force(Rigidbody2D body)
    {
        var angle = (transform.rotation + forceAngle) * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * forceMagnitude;
    }
}

[AddComponentMenu("Physics 2D/Buoyancy Effector 2D")]
public sealed class BuoyancyEffector2D : Effector2D
{
    public Fix64 surfaceLevel { get; set; }
    public Fix64 density { get; set; } = 1;
    public Fix64 linearDrag { get; set; } = 1;
    internal Vector2 Force(Collider2D collider, Rigidbody2D body, Vector2 gravity)
    {
        var bounds = collider.bounds; var bottom = bounds.center.y - bounds.size.y * Fix64.Half;
        var depth = Mathf.Clamp(transform.position.y + surfaceLevel - bottom, 0, bounds.size.y);
        var fraction = bounds.size.y <= Fix64.Epsilon ? Fix64.Zero : depth / bounds.size.y;
        return -gravity * (Fix64.Max(0, density) * bounds.size.x * depth) - body.linearVelocity * (Fix64.Max(0, linearDrag) * fraction);
    }
}
