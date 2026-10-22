namespace BEngine;

public enum ParticleSimulationSpace { Local, World }
public enum ParticleShape2D { Point, Circle, Rectangle }

[AddComponentMenu("Effects/Particle System 2D")]
public sealed partial class ParticleSystem2D : Renderer2D, ISpriteReferenceOwner
{
    private static readonly Material DefaultMaterial = new(Shader.Find("BEngine/Particle2D"))
    {
        name = "Default Particle 2D Material"
    };
    private readonly List<Particle2D> _particles = [];
    private Fix64 _emissionAccumulator;
    private Fix64 _elapsed;
    private ulong _randomState = 1;
    private bool _paused;
    private int _qualityFrame;
    private Fix64 _qualityDelta;
    /// <summary>Opt-in lower-frequency simulation. Larger steps change collision accuracy and presentation timing.</summary>
    public int simulationFrameInterval { get; set; } = 1;

    protected override bool supportsUiSortingLayers => true;

    public Fix64 duration { get; set; } = 5;
    public bool loop { get; set; } = true;
    public bool playOnAwake { get; set; } = true;
    public Fix64 emissionRate { get; set; } = 10;
    public bool prewarm { get; set; }
    public ParticleSimulationSpace simulationSpace { get; set; } = ParticleSimulationSpace.Local;
    public ParticleShape2D shape { get; set; } = ParticleShape2D.Point;
    public Fix64 shapeRadius { get; set; } = 1;
    public Vector2 shapeSize { get; set; } = Vector2.one;
    public Vector2 gravity { get; set; }
    public bool useAutoRandomSeed { get; set; } = true;
    public uint randomSeed { get; set; } = 1;
    public Fix64 startLifetime { get; set; } = 1;
    public Fix64 startLifetimeMin { get; set; }
    public Fix64 startLifetimeMax { get; set; }
    public Fix64 startSpeed { get; set; } = 1;
    public Fix64 startSpeedMin { get; set; }
    public Fix64 startSpeedMax { get; set; }
    public Vector2 startDirection { get; set; } = Vector2.up;
    public Vector2 startSize { get; set; } = Vector2.one;
    public Vector2 startSizeMin { get; set; } = Vector2.one;
    public Vector2 startSizeMax { get; set; } = Vector2.one;
    public Color startColor { get; set; } = Color.white;
    public bool enableColorOverLifetime { get; set; }
    public Color colorOverLifetime { get; set; } = Color.white;
    public bool enableSizeOverLifetime { get; set; }
    public Vector2 sizeOverLifetime { get; set; } = Vector2.one;
    public Fix64 startRotation { get; set; }
    public Fix64 startAngularVelocity { get; set; }
    public int maxParticles { get; set; } = 1000;
    public Sprite? sprite { get; set; }
    public bool isPlaying { get; private set; }
    public bool isPaused => _paused;
    public IReadOnlyList<Particle2D> particles => _particles;
    public Fix64 playbackTime => _elapsed;
    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public bool editorPreview { get; set; }
    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public Fix64 editorPreviewTime { get; set; }

    public Material material
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = DefaultMaterial;

    public override void Start()
    {
        if (!playOnAwake) return;
        if (prewarm && loop && duration > Fix64.Zero)
        {
            Simulate(duration, true);
            isPlaying = true;
        }
        else Play();
    }

    public override void Update()
    {
        if (_paused) { _qualityFrame = 0; _qualityDelta = 0; return; }
        _qualityDelta += Time.deltaTime;
        if (++_qualityFrame < Math.Clamp(simulationFrameInterval, 1, 60)) return;
        var delta = _qualityDelta; _qualityFrame = 0; _qualityDelta = 0; Advance(delta);
    }

    public override void OnValidate()
    {
        duration = Fix64.Max(Fix64.Zero, duration);
        emissionRate = Fix64.Max(Fix64.Zero, emissionRate);
        maxParticles = Math.Max(0, maxParticles);
        startLifetime = Fix64.Max(Fix64.Epsilon, startLifetime);
        startSpeed = Fix64.Max(Fix64.Zero, startSpeed);
        shapeRadius = Fix64.Max(Fix64.Zero, shapeRadius);
        startLifetimeMin = Fix64.Max(Fix64.Zero, startLifetimeMin);
        startLifetimeMax = Fix64.Max(startLifetimeMin, startLifetimeMax);
        startSpeedMin = Fix64.Max(Fix64.Zero, startSpeedMin);
        startSpeedMax = Fix64.Max(startSpeedMin, startSpeedMax);
        ValidateModules();
    }

    /// <summary>Evaluates the system at an arbitrary time for edit-mode previews.</summary>
    public void Simulate(Fix64 time, bool restart = true)
    {
        if (time < 0 || time > 3600) throw new ArgumentOutOfRangeException(nameof(time), "Particle samples must be between 0 and 3600 seconds.");
        var systems = PreviewSystems();
        var saved = systems.Select(system => (System: system, Playing: system.isPlaying, Paused: system._paused)).ToArray();
        foreach (var system in systems)
        {
            if (restart) system.ResetSimulation();
            system.isPlaying = ReferenceEquals(system, this) || system.playOnAwake;
            system._paused = false;
        }
        // Edit-mode previews must emit even when the runtime system is stopped.
        // Restore the previous state after evaluating the requested sample so a
        // preview does not unexpectedly start the live simulation.
        var scopes = new List<IDisposable>();
        try
        {
            foreach (var system in systems)
            {
                if (system.collisionEnabled && system.GetComponent<ParticleCollisionProvider2D>()?.BeginPreview() is { } scope) scopes.Add(scope);
                if (system.isPlaying && !system._startBurstsEmitted && system._elapsed == 0) system.EmitBursts(0, 0);
            }
            var step = Fix64.Parse("0.0166666667");
            using var budget = LoopExecutionGuard.AllowIterations(216001);
            while (_elapsed + step < time)
                foreach (var system in systems) system.Advance(step);
            var remainder = time - _elapsed;
            if (remainder > 0) foreach (var system in systems) system.Advance(remainder);
        }
        finally
        {
            for (var index = scopes.Count - 1; index >= 0; index--) scopes[index].Dispose();
            foreach (var state in saved)
            { state.System.isPlaying = state.Playing && (state.System.loop || state.System._elapsed < state.System.duration); state.System._paused = state.Paused; }
        }
    }

    private void Advance(Fix64 delta)
    {
        if (delta <= Fix64.Zero || _paused) return;
        ValidateModules();
        _collisionProvider = collisionEnabled ? GetComponent<ParticleCollisionProvider2D>() : null;
        if (collisionEnabled && _collisionProvider is not { enabled: true })
            throw new InvalidOperationException("Particle collision requires an enabled ParticleCollisionProvider2D component.");
        if (isPlaying && !_startBurstsEmitted) EmitBursts(_elapsed, _elapsed);
        _pendingCollisions.Clear();
        _trailClock += delta;
        if (!collisionEnabled && subEmitters.Count == 0)
        {
            // No provider/sub-emitter callbacks can mutate storage in this path.
            // Preserve reverse simulation order and stable public particle order while
            // compacting deaths once instead of shifting the tail for every RemoveAt.
            var values = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_particles);
            var removed = false;
            var simple = !trailsEnabled && !enableColorOverLifetime && !enableColorGradient && !enableSizeOverLifetime &&
                !enableSizeCurve && !enableVelocityCurves && !enableRotationCurve && !enableNoise;
            var velocityDelta = simple ? gravity * delta : Vector2.zero;
            var displacement = simple ? gravity * (delta * delta * Fix64.Half) : Vector2.zero;
            for (var index = values.Length - 1; index >= 0; index--)
            {
                ref var current = ref values[index];
                var particle = simple && current.RemainingLifetime >= delta ? current with
                {
                    Position = current.Position + current.Velocity * delta + displacement,
                    Velocity = current.Velocity + velocityDelta,
                    Rotation = current.Rotation + current.AngularVelocity * delta,
                    RemainingLifetime = Fix64.Max(0, current.RemainingLifetime - delta),
                    Color = current.InitialColor, Size = current.InitialSize
                } : StepParticle(current, delta);
                if (!simple) RecordTrail(particle);
                current = particle;
                removed |= particle.RemainingLifetime <= Fix64.Zero;
            }
            if (removed)
            {
                var write = 0;
                for (var index = 0; index < values.Length; index++)
                    if (values[index].RemainingLifetime > Fix64.Zero) values[write++] = values[index];
                _particles.RemoveRange(write, _particles.Count - write);
            }
        }
        else for (var index = _particles.Count - 1; index >= 0; index--)
        {
            var particle = StepParticle(_particles[index], delta);
            RecordTrail(particle);
            if (particle.RemainingLifetime <= Fix64.Zero)
            {
                _particles.RemoveAt(index);
                EmitChildren(ParticleSubEmitterEvent2D.Death, particle);
                continue;
            }
            _particles[index] = particle;
        }
        var previous = _elapsed;
        _elapsed += delta;
        ExpireTrails();
        if (isPlaying && duration > 0)
        {
            var emissionDelta = loop ? delta : Fix64.Max(0, Fix64.Min(duration, _elapsed) - previous);
            _emissionAccumulator += Fix64.Max(0, emissionRate) * emissionDelta;
            var count = (int)_emissionAccumulator;
            _emissionAccumulator -= count;
            Emit(count);
            EmitBursts(previous, _elapsed);
            if (!loop && _elapsed >= duration) isPlaying = false;
        }
        foreach (var collision in _pendingCollisions.ToArray()) particleCollision?.Invoke(collision);
        _pendingCollisions.Clear();
    }

    public void Play()
    {
        ValidateModules();
        if (!isPlaying && !_paused && _elapsed >= duration && duration > Fix64.Zero) ResetSimulation();
        if (_elapsed == 0 && !useAutoRandomSeed) _randomState = randomSeed == 0 ? 1UL : randomSeed;
        isPlaying = true; _paused = false;
        if (!_startBurstsEmitted && _elapsed == 0) EmitBursts(0, 0);
    }
    public void Pause() { isPlaying = false; _paused = true; }
    public void Stop(bool clear = true)
    {
        isPlaying = false; _paused = false;
        _qualityFrame = 0; _qualityDelta = 0;
        _elapsed = Fix64.Zero;
        _emissionAccumulator = Fix64.Zero;
        _startBurstsEmitted = false;
        if (clear) { _particles.Clear(); _trails.Clear(); _trailClock = 0; }
    }

    public void ResetSimulation()
    {
        isPlaying = false; _paused = false; _particles.Clear(); _emissionAccumulator = Fix64.Zero; _elapsed = Fix64.Zero;
        _qualityFrame = 0; _qualityDelta = 0;
        _randomState = useAutoRandomSeed ? 1UL : (randomSeed == 0 ? 1UL : randomSeed);
        _nextParticleId = 0; _startBurstsEmitted = false; _pendingCollisions.Clear();
        _trails.Clear(); _trailClock = 0;
    }

    public void Emit(int count) => EmitCore(count, null);

    private void EmitCore(int count, SpawnContext? context)
    {
        if (count <= 0) return;
        ValidateModules();
        if (_nextParticleId == 0 && !useAutoRandomSeed) _randomState = randomSeed == 0 ? 1UL : randomSeed;
        count = Math.Clamp(count, 0, Math.Max(0, maxParticles - _particles.Count));
        var direction = startDirection.sqrMagnitude > Fix64.Epsilon ? startDirection.normalized : Vector2.up;
        for (var index = 0; index < count; index++)
        {
            var spawn = shape switch
            {
                ParticleShape2D.Circle => RandomInsideCircle(shapeRadius),
                ParticleShape2D.Rectangle => new Vector2(NextRange(-shapeSize.x / 2, shapeSize.x / 2),
                    NextRange(-shapeSize.y / 2, shapeSize.y / 2)),
                _ => Vector2.zero
            };
            var speed = NextRange(startSpeedMin <= Fix64.Zero ? startSpeed : startSpeedMin,
                startSpeedMax <= Fix64.Zero ? startSpeed : startSpeedMax);
            var lifetime = Fix64.Max(Fix64.Epsilon, NextRange(
                startLifetimeMin <= Fix64.Zero ? startLifetime : startLifetimeMin,
                startLifetimeMax <= Fix64.Zero ? startLifetime : startLifetimeMax));
            var size = startSizeMin == Vector2.one && startSizeMax == Vector2.one ? startSize :
                new Vector2(NextRange(startSizeMin.x, startSizeMax.x), NextRange(startSizeMin.y, startSizeMax.y));
            var velocity = shape == ParticleShape2D.Circle && spawn.sqrMagnitude > Fix64.Epsilon ? spawn.normalized * speed : direction * speed;
            size = size.sqrMagnitude <= Fix64.Epsilon ? startSize : size;
            var rotation = startRotation;
            if (simulationSpace == ParticleSimulationSpace.World)
            {
                spawn = transform.TransformPoint(spawn); velocity = transform.TransformVector(velocity);
                rotation += transform.rotation; size = Vector2.Scale(size, transform.lossyScale);
            }
            var color = startColor;
            if (context is { } inherited)
            {
                spawn += simulationSpace == ParticleSimulationSpace.World ? inherited.Position - transform.position : transform.InverseTransformPoint(inherited.Position);
                velocity += simulationSpace == ParticleSimulationSpace.World ? inherited.Velocity : transform.InverseTransformVector(inherited.Velocity);
                color *= inherited.Color;
            }
            var particle = new Particle2D(spawn, velocity, rotation, startAngularVelocity, size, color, lifetime)
            { Lifetime = lifetime, Id = ++_nextParticleId };
            _particles.Add(particle);
            RecordTrail(particle);
            EmitChildren(ParticleSubEmitterEvent2D.Birth, particle);
        }
    }

    private Vector2 RandomInsideCircle(Fix64 radius)
    {
        var angle = NextRange(Fix64.Zero, Fix64.TwoPi);
        var distance = Fix64.Sqrt(NextRange(Fix64.Zero, Fix64.One)) * Fix64.Max(Fix64.Zero, radius);
        return new Vector2(Fix64.Cos(angle), Fix64.Sin(angle)) * distance;
    }
    private Fix64 NextRange(Fix64 min, Fix64 max)
    {
        if (max <= min) return min;
        _randomState ^= _randomState << 7; _randomState ^= _randomState >> 9;
        var value = Fix64.FromRaw((long)(_randomState >> 32));
        return min + (max - min) * value;
    }
    private static Color Lerp(Color start, Color end, Fix64 t) => new(
        Mathf.Lerp(start.r, end.r, t), Mathf.Lerp(start.g, end.g, t),
        Mathf.Lerp(start.b, end.b, t), Mathf.Lerp(start.a, end.a, t));

    internal SpriteRenderData2D ResolveSpriteUnchecked() => SpriteSourceRegistry.Resolve(sprite);
    internal RenderBatchKey2D BatchKeyUnchecked => BatchKey(ResolveSpriteUnchecked());
    internal RenderBatchKey2D BatchKey(SpriteRenderData2D visual) => new(material, visual.BatchIdentity);
}
