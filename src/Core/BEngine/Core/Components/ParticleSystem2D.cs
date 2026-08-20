namespace BEngine;

[AddComponentMenu("Effects/Particle System 2D")]
public sealed class ParticleSystem2D : Renderer2D
{
    private static readonly Material DefaultMaterial = new(Shader.Find("BEngine/Particle2D"))
    {
        name = "Default Particle 2D Material"
    };
    private readonly List<Particle2D> _particles = [];
    private Material _material = DefaultMaterial;
    private Fix64 _emissionAccumulator;

    protected override bool supportsUiSortingLayers => true;

    public Fix64 duration { get; set; } = 5;
    public bool loop { get; set; } = true;
    public bool playOnAwake { get; set; } = true;
    public Fix64 emissionRate { get; set; } = 10;
    public Fix64 startLifetime { get; set; } = 1;
    public Fix64 startSpeed { get; set; } = 1;
    public Vector2 startDirection { get; set; } = Vector2.up;
    public Vector2 startSize { get; set; } = Vector2.one;
    public Color startColor { get; set; } = Color.white;
    public Fix64 startRotation { get; set; }
    public Fix64 startAngularVelocity { get; set; }
    public int maxParticles { get; set; } = 1000;
    public string sprite { get; set; } = string.Empty;
    public string atlas { get; set; } = string.Empty;
    public bool isPlaying { get; private set; }
    public IReadOnlyList<Particle2D> particles => _particles;

    public Material material
    {
        get { MainThreadGuard.Ensure(); return _material; }
        set { MainThreadGuard.Ensure(); _material = value ?? throw new ArgumentNullException(nameof(value)); }
    }

    public override void Start()
    {
        if (playOnAwake) Play();
    }

    public override void Update()
    {
        var delta = Time.deltaTime;
        for (var index = _particles.Count - 1; index >= 0; index--)
        {
            var particle = _particles[index];
            var lifetime = particle.RemainingLifetime - delta;
            if (lifetime <= Fix64.Zero)
            {
                _particles.RemoveAt(index);
                continue;
            }
            _particles[index] = particle with
            {
                Position = particle.Position + particle.Velocity * delta,
                Rotation = particle.Rotation + particle.AngularVelocity * delta,
                RemainingLifetime = lifetime
            };
        }
        if (!isPlaying || emissionRate <= Fix64.Zero) return;
        _emissionAccumulator += emissionRate * delta;
        var count = (int)_emissionAccumulator;
        _emissionAccumulator -= count;
        Emit(count);
    }

    public void Play() => isPlaying = true;
    public void Pause() => isPlaying = false;
    public void Stop(bool clear = true)
    {
        isPlaying = false;
        _emissionAccumulator = Fix64.Zero;
        if (clear) _particles.Clear();
    }

    public void Emit(int count)
    {
        count = Math.Clamp(count, 0, Math.Max(0, maxParticles - _particles.Count));
        var direction = startDirection.sqrMagnitude > Fix64.Epsilon
            ? startDirection.normalized
            : Vector2.up;
        for (var index = 0; index < count; index++)
            _particles.Add(new Particle2D(Vector2.zero, direction * startSpeed,
                startRotation, startAngularVelocity, startSize, startColor,
                Fix64.Max(Fix64.Epsilon, startLifetime)));
    }

    internal SpriteRenderData2D ResolveSpriteUnchecked() => TextureAtlasResolver.Resolve(atlas, sprite);
    internal RenderBatchKey2D BatchKeyUnchecked => BatchKey(ResolveSpriteUnchecked());
    internal RenderBatchKey2D BatchKey(SpriteRenderData2D visual) => new(_material, visual.BatchIdentity);
}
