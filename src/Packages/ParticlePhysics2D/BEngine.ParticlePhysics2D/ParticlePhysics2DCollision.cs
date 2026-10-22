using BEngine.Physics2D;
using Physics = BEngine.Physics2D.Physics2D;

namespace BEngine;

[AddComponentMenu("Effects/Particle Physics 2D Collision")]
[RequireComponent(typeof(ParticleSystem2D))]
[DisallowMultipleComponent]
public sealed class ParticlePhysics2DCollision : ParticleCollisionProvider2D
{
    public LayerMask collisionMask { get; set; } = new(ulong.MaxValue);
    public bool includeTriggers { get; set; }

    public override IDisposable? BeginPreview() => gameObject.scene is { } scene ? PhysicsWorld2D.OpenQueryScope(scene) : null;

    public override bool Cast(Vector2 from, Vector2 to, Fix64 radius, out ParticleCollisionHit2D hit)
    {
        hit = default;
        var distance = Vector2.Distance(from, to);
        if (distance <= Fix64.Epsilon) return false;
        var direction = (to - from) / distance;
        var query = includeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;
        RaycastHit2D result;
        var collided = radius > 0
            ? Physics.CircleCast(from, radius, direction, out result, distance, collisionMask.value, query)
            : Physics.Raycast(from, direction, out result, distance, collisionMask.value, query);
        if (!collided) return false;
        hit = new(from + direction * result.distance, result.normal, result.collider);
        return true;
    }
}
