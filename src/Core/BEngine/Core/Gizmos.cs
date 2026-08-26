namespace BEngine;

public static class Gizmos
{
    private const int CircleSegments = 48;
    private static readonly Fix64 DefaultLineWidth = 2;
    private static GizmoDrawList? _drawList;

    public static Color color { get; set; } = Color.white;
    public static Fix64 lineWidth { get; set; } = DefaultLineWidth;

    public static void DrawLine(Vector2 from, Vector2 to) =>
        _drawList?.Add(new GizmoLine2D(from, to, color,
            Fix64.Max(Fix64.One, lineWidth)));

    public static void DrawRay(Vector2 from, Vector2 direction) => DrawLine(from, from + direction);

    public static void DrawWireCube(Vector2 center, Vector2 size) =>
        DrawWireCube(center, size, Fix64.Zero);

    public static void DrawWireCube(Vector2 center, Vector2 size, Fix64 rotation)
    {
        var half = new Vector2(Fix64.Abs(size.x), Fix64.Abs(size.y)) * Fix64.Half;
        var points = new[]
        {
            new Vector2(-half.x, -half.y),
            new Vector2(half.x, -half.y),
            new Vector2(half.x, half.y),
            new Vector2(-half.x, half.y)
        };
        for (var index = 0; index < points.Length; index++)
        {
            var from = center + Transform.RotateVector(points[index], rotation);
            var to = center + Transform.RotateVector(points[(index + 1) % points.Length], rotation);
            DrawLine(from, to);
        }
    }

    public static void DrawWireSphere(Vector2 center, Fix64 radius)
    {
        radius = Fix64.Abs(radius);
        if (radius <= Fix64.Epsilon) return;
        var previous = center + Vector2.right * radius;
        for (var index = 1; index <= CircleSegments; index++)
        {
            var angle = Fix64.TwoPi * index / CircleSegments;
            var next = center + new Vector2(Fix64.Cos(angle), Fix64.Sin(angle)) * radius;
            DrawLine(previous, next);
            previous = next;
        }
    }

    public static void DrawArrow(Vector2 from, Vector2 to)
    {
        var delta = to - from;
        var length = delta.magnitude;
        if (length <= Fix64.Epsilon) return;
        DrawLine(from, to);
        var direction = delta / length;
        var headLength = Fix64.Min(length * Fix64.Parse("0.25"), Fix64.Half);
        var left = Transform.RotateVector(-direction, 28) * headLength;
        var right = Transform.RotateVector(-direction, -28) * headLength;
        DrawLine(to, to + left);
        DrawLine(to, to + right);
    }

    internal static GizmoCollectionScope BeginCollection(GizmoDrawList drawList)
    {
        ArgumentNullException.ThrowIfNull(drawList);
        var scope = new GizmoCollectionScope(_drawList, color, lineWidth);
        _drawList = drawList;
        ResetState();
        return scope;
    }

    internal static void ResetState()
    {
        color = Color.white;
        lineWidth = DefaultLineWidth;
    }

    internal readonly struct GizmoCollectionScope(
        GizmoDrawList? previous,
        Color previousColor,
        Fix64 previousLineWidth) : IDisposable
    {
        public void Dispose()
        {
            _drawList = previous;
            color = previousColor;
            lineWidth = previousLineWidth;
        }
    }
}

internal sealed class GizmoDrawList
{
    private readonly List<GizmoLine2D> _lines = [];

    internal IReadOnlyList<GizmoLine2D> Lines => _lines;
    internal void Add(GizmoLine2D line) => _lines.Add(line);
}

internal readonly record struct GizmoLine2D(
    Vector2 From,
    Vector2 To,
    Color Color,
    Fix64 LineWidth);
