namespace BEngine;

[AddComponentMenu("Rendering/Trail Renderer 2D")]
public sealed class TrailRenderer2D : Renderer2D
{
    private static readonly Material DefaultMaterial =
        Material.GetBuiltIn("BEngine/Sprite", "Default Trail Material");
    [NonSerialized] private readonly List<TrailPoint> _points = [];

    public Fix64 time { get; set; } = Fix64.One;
    public Fix64 minVertexDistance { get; set; } = Fix64.Parse("0.1");
    public Fix64 startWidth { get; set; } = Fix64.Parse("0.2");
    public Fix64 endWidth { get; set; }
    public Color startColor { get; set; } = Color.white;
    public Color endColor { get; set; } = new(1, 1, 1, 0);
    public bool emitting { get; set; } = true;
    public bool autoDestruct { get; set; }
    public Material material
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = DefaultMaterial;
    public int positionCount => _points.Count;

    public override void OnEnable()
    {
        if (emitting) AddPosition(transform.position);
    }

    public override void Update()
    {
        RemoveExpired();
        if (emitting && (_points.Count == 0 ||
                         Vector2.Distance(_points[^1].Position, transform.position) >= minVertexDistance))
            AddPosition(transform.position);
        if (autoDestruct && !emitting && _points.Count == 0) Destroy(gameObject);
    }

    public void AddPosition(Vector2 position) => _points.Add(new TrailPoint(position, Time.time));

    public void Clear() => _points.Clear();

    public Vector2 GetPosition(int index) => _points[index].Position;

    internal void FillSegments(ICollection<LineSegment2D> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        RemoveExpired();
        var segmentCount = _points.Count - 1;
        for (var index = 0; index < segmentCount; index++)
        {
            var from = _points[index].Position;
            var to = _points[index + 1].Position;
            var delta = to - from;
            var length = delta.magnitude;
            if (length <= Fix64.Epsilon) continue;
            var normalizedAge = time <= Fix64.Epsilon
                ? Fix64.One
                : Mathf.Clamp01((Time.time - (_points[index].Time + _points[index + 1].Time) * Fix64.Half) / time);
            var width = Fix64.Max(Fix64.Epsilon, Mathf.Lerp(startWidth, endWidth, normalizedAge));
            output.Add(new LineSegment2D((from + to) * Fix64.Half,
                Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg,
                new Vector2(length, width), LineRenderer2D.Lerp(startColor, endColor, normalizedAge)));
        }
    }

    private void RemoveExpired()
    {
        var cutoff = Time.time - Fix64.Max(Fix64.Zero, time);
        var removeCount = 0;
        while (removeCount < _points.Count && _points[removeCount].Time < cutoff) removeCount++;
        if (removeCount > 0) _points.RemoveRange(0, removeCount);
    }

    private readonly record struct TrailPoint(Vector2 Position, Fix64 Time);
}
