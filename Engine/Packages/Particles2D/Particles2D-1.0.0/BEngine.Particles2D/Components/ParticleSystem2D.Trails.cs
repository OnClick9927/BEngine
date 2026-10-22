using BEngine.Rendering;

namespace BEngine;

public readonly record struct ParticleTrailSegment2D(Vector2 From, Vector2 To, Fix64 Width, Color Color);

public sealed partial class ParticleSystem2D
{
    private readonly Dictionary<ulong, TrailBuffer> _trails = [];
    private readonly HashSet<ulong> _aliveTrailParticles = [];
    private readonly List<ulong> _expiredTrails = [];
    private Fix64 _trailClock;
    public bool trailsEnabled { get; set; }
    public Fix64 trailLifetime { get; set; } = (Fix64).5;
    public Fix64 trailWidth { get; set; } = (Fix64).1;
    public Fix64 trailMinDistance { get; set; } = (Fix64).01;
    public int trailMaxPoints { get; set; } = 64;
    public bool trailDieWithParticles { get; set; }
    public AnimationCurve trailWidthCurve { get; set; } = AnimationCurve.Linear(0, 0, 1, 1);

    public IEnumerable<ParticleTrailSegment2D> GetTrailSegments()
    {
        if (!trailsEnabled || trailLifetime <= 0) yield break;
        foreach (var points in _trails.Values)
        for (var index = 1; index < points.Count; index++)
        {
            var point = points[index];
            var fade = 1 - Mathf.Clamp01((_trailClock - point.Time) / trailLifetime);
            var width = Fix64.Max(0, trailWidth * trailWidthCurve.Evaluate((Fix64)index / (points.Count - 1)));
            yield return new(points[index - 1].Position, point.Position, width,
                new Color(point.Color.r, point.Color.g, point.Color.b, point.Color.a * fade));
        }
    }

    private void RecordTrail(Particle2D particle)
    {
        if (!trailsEnabled || trailLifetime <= 0) return;
        if (!_trails.TryGetValue(particle.Id, out var points)) _trails[particle.Id] = points = new();
        if (points.Count > 0 && Vector2.Distance(points[^1].Position, particle.Position) < Fix64.Max((Fix64).00001, trailMinDistance)) return;
        points.Add(new(particle.Position, _trailClock, particle.Color), Math.Clamp(trailMaxPoints, 2, 1024));
    }

    private void ExpireTrails()
    {
        if (!trailsEnabled || trailLifetime <= 0) { _trails.Clear(); return; }
        _aliveTrailParticles.Clear(); _expiredTrails.Clear();
        if (trailDieWithParticles)
            foreach (var particle in _particles) _aliveTrailParticles.Add(particle.Id);
        foreach (var (id, points) in _trails)
        {
            points.Expire(_trailClock, trailLifetime);
            if (points.Count == 0 || trailDieWithParticles && !_aliveTrailParticles.Contains(id)) _expiredTrails.Add(id);
        }
        foreach (var id in _expiredTrails) _trails.Remove(id);
        _expiredTrails.Clear(); _aliveTrailParticles.Clear();
    }

    private void CollectTrails(ISceneGeometrySink2D sink)
    {
        // Iterator methods allocate before their first MoveNext, even when
        // their body immediately yields no segments. Idle systems need none.
        if (!trailsEnabled || trailLifetime <= 0 || _trails.Count == 0) return;
        foreach (var segment in GetTrailSegments())
        {
            var world = simulationSpace == ParticleSimulationSpace.World;
            var from = world ? segment.From : transform.TransformPoint(segment.From);
            var to = world ? segment.To : transform.TransformPoint(segment.To);
            var direction = to - from; var length = direction.magnitude;
            if (length <= Fix64.Epsilon || segment.Width <= 0) continue;
            var scale = world ? Fix64.One : (Fix64.Abs(transform.lossyScale.x) + Fix64.Abs(transform.lossyScale.y)) * Fix64.Half;
            var center = (from + to) * Fix64.Half;
            var size = new Vector2(length, segment.Width * scale);
            var rotation = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            if (!sink.Camera.IsVisible(center, rotation, size, sink.Width, sink.Height)) continue;
            sink.Submit(this, SceneQuad2D.Create(center, rotation, size, WithOpacity(segment.Color, opacity),
                string.Empty, new Rect(0, 0, 1, 1), false, false, false), new RenderBatchKey2D(material, string.Empty));
        }
    }

    private readonly record struct TrailPoint(Vector2 Position, Fix64 Time, Color Color);

    private sealed class TrailBuffer
    {
        private TrailPoint[] _points = [];
        private int _head;
        internal int Count { get; private set; }
        internal TrailPoint this[int index] => _points[(_head + (index < 0 ? Count + index : index)) % _points.Length];
        internal void Add(TrailPoint point, int limit)
        {
            while (Count >= limit) { _head = (_head + 1) % _points.Length; Count--; }
            if (Count == _points.Length)
            {
                var grown = new TrailPoint[Math.Min(limit, Math.Max(4, _points.Length * 2))];
                for (var index = 0; index < Count; index++) grown[index] = this[index];
                _points = grown; _head = 0;
            }
            _points[(_head + Count) % _points.Length] = point; Count++;
        }
        internal void Expire(Fix64 now, Fix64 lifetime)
        { while (Count > 0 && now - this[0].Time >= lifetime) { _head = (_head + 1) % _points.Length; Count--; } }
    }
}
