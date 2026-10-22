namespace BEngine;

public sealed class ParticleBurst2D
{
    public Fix64 time { get; set; }
    public int count { get; set; } = 10;
    public int cycles { get; set; } = 1;
    public Fix64 interval { get; set; } = 1;
    public Fix64 probability { get; set; } = 1;
}

public enum ParticleSubEmitterEvent2D { Birth, Death, Collision }

public sealed class ParticleSubEmitter2D
{
    public ParticleSystem2D? system { get; set; }
    public ParticleSubEmitterEvent2D trigger { get; set; } = ParticleSubEmitterEvent2D.Death;
    public int count { get; set; } = 1;
    public bool inheritColor { get; set; } = true;
    public bool inheritVelocity { get; set; }
}

public sealed class ParticleColorKey2D
{
    public Fix64 time { get; set; }
    public Color color { get; set; } = Color.white;
}

public readonly record struct ParticleCollisionHit2D(Vector2 Position, Vector2 Normal, BObject? Collider = null);
public readonly record struct ParticleCollisionEvent2D(Particle2D Particle, ParticleCollisionHit2D Hit);

/// <summary>Optional world-space swept collision provider. Particles2D has no Physics2D dependency.</summary>
public abstract class ParticleCollisionProvider2D : Component
{
    public virtual IDisposable? BeginPreview() => null;
    public abstract bool Cast(Vector2 from, Vector2 to, Fix64 radius, out ParticleCollisionHit2D hit);
}
