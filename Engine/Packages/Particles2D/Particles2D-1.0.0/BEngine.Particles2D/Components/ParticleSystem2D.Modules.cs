namespace BEngine;

public sealed partial class ParticleSystem2D
{
    private ulong _nextParticleId;
    private bool _startBurstsEmitted;
    private readonly List<ParticleCollisionEvent2D> _pendingCollisions = [];
    private ParticleCollisionProvider2D? _collisionProvider;
    public List<ParticleBurst2D> bursts { get; set; } = [];
    public List<ParticleSubEmitter2D> subEmitters { get; set; } = [];
    public bool enableSizeCurve { get; set; }
    public AnimationCurve sizeCurve { get; set; } = AnimationCurve.Linear(0, 1, 1, 0);
    public bool enableColorGradient { get; set; }
    public List<ParticleColorKey2D> colorGradient { get; set; } = [new(), new() { time = 1, color = new(1, 1, 1, 0) }];
    public bool enableVelocityCurves { get; set; }
    public AnimationCurve velocityX { get; set; } = AnimationCurve.Linear(0, 0, 1, 0);
    public AnimationCurve velocityY { get; set; } = AnimationCurve.Linear(0, 0, 1, 0);
    public bool enableRotationCurve { get; set; }
    public AnimationCurve angularVelocityCurve { get; set; } = AnimationCurve.Linear(0, 0, 1, 0);
    public bool enableNoise { get; set; }
    public Fix64 noiseStrength { get; set; } = 1;
    public Fix64 noiseFrequency { get; set; } = 1;
    public Fix64 noiseSpeed { get; set; } = 1;
    public bool collisionEnabled { get; set; }
    public Fix64 collisionRadius { get; set; } = (Fix64).05;
    public Fix64 collisionBounce { get; set; } = (Fix64).5;
    public Fix64 collisionDamping { get; set; }
    public Fix64 collisionLifetimeLoss { get; set; }
    public event Action<ParticleCollisionEvent2D>? particleCollision;

    private void ValidateModules()
    {
        if (sizeCurve is null || velocityX is null || velocityY is null || angularVelocityCurve is null || trailWidthCurve is null)
            throw new InvalidOperationException("Particle curves cannot be null.");
        if (bursts is null || bursts.Count > 256 || bursts.Any(b => b is null || b.time < 0 || b.count < 0 || b.count > 100000 ||
            b.cycles is < 1 or > 1024 || b.cycles > 1 && b.interval <= 0 || b.probability < 0 || b.probability > 1))
            throw new InvalidOperationException("Invalid particle burst settings.");
        if (colorGradient is null || colorGradient.Count > 64 || colorGradient.Any(key => key is null || key.time < 0 || key.time > 1))
            throw new InvalidOperationException("Particle gradient keys must be within normalized lifetime 0..1.");
        if (subEmitters is { Count: 0 }) return;
        var path = new HashSet<ParticleSystem2D>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<ParticleSystem2D>(ReferenceEqualityComparer.Instance);
        Visit(this);
        void Visit(ParticleSystem2D system)
        {
            if (!path.Add(system)) throw new InvalidOperationException("Cyclic particle sub-emitter references.");
            if (path.Count > 64) throw new InvalidOperationException("Particle sub-emitter nesting exceeds 64 systems.");
            if (system.subEmitters is null || system.subEmitters.Count > 64 || system.subEmitters.Any(child => child is null ||
                child.count < 0 || child.count > 100000 || !Enum.IsDefined(child.trigger)))
                throw new InvalidOperationException("Invalid particle sub-emitter settings.");
            foreach (var child in system.subEmitters)
                if (child.system is { isDestroyed: false } target)
                {
                    if (!ReferenceEquals(target.gameObject.scene, system.gameObject.scene)) throw new InvalidOperationException("Sub-emitters must belong to the same scene.");
                    if (!visited.Contains(target)) Visit(target);
                }
            path.Remove(system); visited.Add(system);
        }
    }

    internal IReadOnlyList<ParticleSystem2D> PreviewSystems()
    {
        ValidateModules();
        var systems = new List<ParticleSystem2D>();
        var visited = new HashSet<ParticleSystem2D>(ReferenceEqualityComparer.Instance);
        Visit(this); systems.Reverse(); return systems;
        void Visit(ParticleSystem2D system)
        {
            if (!visited.Add(system)) return;
            foreach (var child in system.subEmitters)
                if (child.system is { isDestroyed: false, enabled: true } target && target.gameObject.activeInHierarchy) Visit(target);
            systems.Add(system);
        }
    }

    private void EmitBursts(Fix64 from, Fix64 to)
    {
        if (bursts.Count == 0 || duration <= 0) { _startBurstsEmitted = true; return; }
        var firstCycle = loop ? (long)(from / duration) : 0;
        var lastCycle = loop ? (long)(to / duration) : 0;
        if (lastCycle - firstCycle > 1024) throw new InvalidOperationException("Particle update crosses too many emission cycles.");
        using var budget = LoopExecutionGuard.AllowIterations(262145);
        for (var cycle = firstCycle; cycle <= lastCycle; cycle++)
        foreach (var burst in bursts)
        for (var repeat = 0; repeat < burst.cycles; repeat++)
        {
            var phase = burst.time + burst.interval * repeat;
            if (phase >= duration) break;
            var at = cycle * duration + phase;
            if (at > to || at < from || at == from && (at != 0 || _startBurstsEmitted)) continue;
            if (burst.probability == 1 || NextRange(0, 1) < burst.probability) Emit(burst.count);
        }
        _startBurstsEmitted = true;
    }

    private Particle2D StepParticle(Particle2D particle, Fix64 delta)
    {
        var step = Fix64.Min(delta, particle.RemainingLifetime);
        var remaining = Fix64.Max(0, particle.RemainingLifetime - delta);
        var needsAge = enableColorOverLifetime || enableColorGradient || enableSizeOverLifetime ||
            enableSizeCurve || enableVelocityCurves || enableRotationCurve;
        var age = !needsAge ? Fix64.Zero : particle.Lifetime <= Fix64.Epsilon ? Fix64.One : Mathf.Clamp01((particle.Lifetime - remaining) / particle.Lifetime);
        var color = enableColorOverLifetime ? Lerp(particle.InitialColor, colorOverLifetime, age) : particle.InitialColor;
        if (enableColorGradient) color *= SampleGradient(age);
        var size = enableSizeOverLifetime ? Vector2.Lerp(particle.InitialSize, Vector2.Scale(particle.InitialSize, sizeOverLifetime), age) : particle.InitialSize;
        if (enableSizeCurve) size *= Fix64.Max(0, sizeCurve.Evaluate(age));
        var acceleration = gravity;
        if (enableNoise)
        {
            var phase = (Fix64)(long)(particle.Id % 65536) * (Fix64).618 + _elapsed * noiseSpeed;
            acceleration += new Vector2(Mathf.Sin(particle.Position.y * noiseFrequency + phase),
                Mathf.Cos(particle.Position.x * noiseFrequency + phase)) * noiseStrength;
        }
        var extraVelocity = enableVelocityCurves ? new Vector2(velocityX.Evaluate(age), velocityY.Evaluate(age)) : Vector2.zero;
        var next = particle with
        {
            Position = particle.Position + (particle.Velocity + extraVelocity) * step + acceleration * (step * step * Fix64.Half),
            Velocity = particle.Velocity + acceleration * step,
            Rotation = particle.Rotation + (particle.AngularVelocity + (enableRotationCurve ? angularVelocityCurve.Evaluate(age) : 0)) * step,
            RemainingLifetime = remaining, Color = color, Size = size
        };
        if (collisionEnabled && _collisionProvider is not null)
        {
            var world = simulationSpace == ParticleSimulationSpace.World;
            var from = world ? particle.Position : transform.TransformPoint(particle.Position);
            var to = world ? next.Position : transform.TransformPoint(next.Position);
            if (_collisionProvider.Cast(from, to, Fix64.Max(0, collisionRadius), out var hit))
            {
                var normal = hit.Normal.normalized;
                var velocity = world ? next.Velocity : transform.TransformVector(next.Velocity);
                var inward = Vector2.Dot(velocity, normal);
                if (inward < 0) velocity -= normal * inward * (1 + Mathf.Clamp01(collisionBounce));
                velocity *= 1 - Mathf.Clamp01(collisionDamping);
                var position = hit.Position + normal * (Fix64).0001;
                next = next with
                {
                    Position = world ? position : transform.InverseTransformPoint(position),
                    Velocity = world ? velocity : transform.InverseTransformVector(velocity),
                    RemainingLifetime = Fix64.Max(0, next.RemainingLifetime - particle.Lifetime * Mathf.Clamp01(collisionLifetimeLoss))
                };
                EmitChildren(ParticleSubEmitterEvent2D.Collision, next);
                _pendingCollisions.Add(new(next, hit));
            }
        }
        return next;
    }

    private Color SampleGradient(Fix64 age)
    {
        ParticleColorKey2D? left = null, right = null;
        foreach (var key in colorGradient)
        {
            if (key.time <= age && (left is null || key.time >= left.time)) left = key;
            if (key.time >= age && (right is null || key.time < right.time)) right = key;
        }
        if (left is null) return right?.color ?? Color.white;
        if (right is null || right.time == left.time) return left.color;
        return Lerp(left.color, right.color, (age - left.time) / (right.time - left.time));
    }

    private void EmitChildren(ParticleSubEmitterEvent2D trigger, Particle2D particle)
    {
        foreach (var child in subEmitters)
        {
            if (child.trigger != trigger || child.system is not { isDestroyed: false, enabled: true } target || !target.gameObject.activeInHierarchy) continue;
            if (!ReferenceEquals(target.gameObject.scene, gameObject.scene)) throw new InvalidOperationException("Sub-emitters must belong to the same scene.");
            var world = simulationSpace == ParticleSimulationSpace.World;
            target.EmitCore(child.count, new SpawnContext(world ? particle.Position : transform.TransformPoint(particle.Position),
                child.inheritVelocity ? (world ? particle.Velocity : transform.TransformVector(particle.Velocity)) : Vector2.zero,
                child.inheritColor ? particle.Color : Color.white));
        }
    }

    private readonly record struct SpawnContext(Vector2 Position, Vector2 Velocity, Color Color);
}
