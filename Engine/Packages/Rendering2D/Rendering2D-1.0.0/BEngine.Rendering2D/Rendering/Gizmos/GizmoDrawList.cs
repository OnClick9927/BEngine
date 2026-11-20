namespace BEngine;

internal sealed class GizmoDrawList
{
    private readonly List<GizmoLine2D> _lines = [];

    internal IReadOnlyList<GizmoLine2D> Lines => _lines;
    internal void Add(GizmoLine2D line) => _lines.Add(line);
}
