using System.Runtime.InteropServices;

namespace BEngine;

[AddComponentMenu("Rendering/Line Renderer 2D")]
public sealed class LineRenderer2D : Renderer2D
{
    private static readonly Material DefaultMaterial =
        Material.GetBuiltIn("BEngine/Sprite", "Default Line Material");

    [SerializeField] private List<Vector2> _positions = [];

    public int positionCount
    {
        get => _positions.Count;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (value < _positions.Count) _positions.RemoveRange(value, _positions.Count - value);
            else while (_positions.Count < value) _positions.Add(Vector2.zero);
        }
    }

    public bool loop { get; set; }
    public bool useWorldSpace { get; set; }
    public Fix64 startWidth { get; set; } = Fix64.Parse("0.1");
    public Fix64 endWidth { get; set; } = Fix64.Parse("0.1");
    public Color startColor { get; set; } = Color.white;
    public Color endColor { get; set; } = Color.white;
    public Material material
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = DefaultMaterial;

    public Vector2 GetPosition(int index) => _positions[index];

    public void SetPosition(int index, Vector2 position) => _positions[index] = position;

    public void SetPositions(IEnumerable<Vector2> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        _positions = [.. positions];
    }

    public int GetPositions(Span<Vector2> positions)
    {
        var count = Math.Min(positions.Length, _positions.Count);
        CollectionsMarshal.AsSpan(_positions)[..count].CopyTo(positions);
        return count;
    }

    internal void FillSegments(ICollection<LineSegment2D> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var segmentCount = loop && _positions.Count > 2 ? _positions.Count : _positions.Count - 1;
        for (var index = 0; index < segmentCount; index++)
        {
            var from = ResolvePoint(_positions[index]);
            var to = ResolvePoint(_positions[(index + 1) % _positions.Count]);
            AddSegment(output, from, to, index, Math.Max(1, segmentCount));
        }
    }

    private Vector2 ResolvePoint(Vector2 point) => useWorldSpace ? point : transform.TransformPoint(point);

    private void AddSegment(
        ICollection<LineSegment2D> output,
        Vector2 from,
        Vector2 to,
        int index,
        int segmentCount)
    {
        var delta = to - from;
        var length = delta.magnitude;
        if (length <= Fix64.Epsilon) return;
        var time = ((Fix64)index + Fix64.Half) / segmentCount;
        var width = Fix64.Max(Fix64.Epsilon, Mathf.Lerp(startWidth, endWidth, time));
        output.Add(new LineSegment2D((from + to) * Fix64.Half,
            Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg,
            new Vector2(length, width), Lerp(startColor, endColor, time)));
    }

    internal static Color Lerp(Color from, Color to, Fix64 time) => new(
        Mathf.Lerp(from.r, to.r, time),
        Mathf.Lerp(from.g, to.g, time),
        Mathf.Lerp(from.b, to.b, time),
        Mathf.Lerp(from.a, to.a, time));
}
