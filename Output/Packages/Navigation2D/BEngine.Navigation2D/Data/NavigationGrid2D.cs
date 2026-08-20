using BEngine.Physics2D;

namespace BEngine.Navigation2D;

internal sealed class NavigationGrid2D
{
    private readonly NavigationSurface2D _surface;
    private readonly int _width;
    private readonly int _height;
    private readonly bool[] _walkable;
    private readonly Vector2 _origin;
    private readonly Fix64 _cellSize;

    private NavigationGrid2D(
        NavigationSurface2D surface, int width, int height, Vector2 origin, Fix64 cellSize)
    {
        _surface = surface;
        _width = width;
        _height = height;
        _origin = origin;
        _cellSize = cellSize;
        _walkable = new bool[width * height];
    }

    public static NavigationGrid2D Bake(NavigationSurface2D surface)
    {
        var cell = Mathf.Max(surface.cellSize, Fix64.Parse("0.1"));
        var width = Math.Clamp((int)(surface.size.x / cell) + 1, 2, 1024);
        var height = Math.Clamp((int)(surface.size.y / cell) + 1, 2, 1024);
        var origin = surface.transform.TransformPoint(surface.center - surface.size / 2);
        var grid = new NavigationGrid2D(surface, width, height, origin, cell);
        var scene = surface.gameObject.scene;
        var obstacles = scene?.QueryComponents<NavigationObstacle2D>().ToArray()
            .Where(IsEnabled).Where(item => item.carving).ToArray() ?? [];
        var volumes = scene?.QueryComponents<NavigationModifierVolume2D>().ToArray()
            .Where(IsEnabled).ToArray() ?? [];
        var modifiers = scene?.QueryComponents<NavigationModifier2D>().ToArray()
            .Where(IsEnabled).ToArray() ?? [];
        var colliders = surface.useGeometry == NavigationCollectGeometry2D.PhysicsColliders
            ? scene?.QueryComponents<Collider2D>().ToArray()
                .Where(IsEnabled)
                .Where(item => !item.isTrigger && (surface.layerMask & item.gameObject.layer) != 0)
                .ToArray() ?? []
            : [];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var point = new Vector2(origin.x + x * cell, origin.y + y * cell);
            var walkable = !obstacles.Any(item => item.Contains(point, surface.agentRadius)) &&
                           !volumes.Any(item => item.area == 1 && item.Contains(point)) &&
                           !modifiers.Any(item => item.Blocks(point, surface.agentTypeId)) &&
                           !colliders.Any(item => ExpandedContains(item.bounds, point, surface.agentRadius));
            grid._walkable[y * width + x] = walkable;
        }
        return grid;
    }

    public bool Sample(Vector2 source, Fix64 maxDistance, out NavigationHit2D hit)
    {
        var start = ToCell(source);
        var radius = Math.Max(1, (int)(maxDistance / _cellSize));
        var best = -1;
        var bestDistance = maxDistance + Fix64.One;
        for (var y = Math.Max(0, start.Y - radius); y <= Math.Min(_height - 1, start.Y + radius); y++)
        for (var x = Math.Max(0, start.X - radius); x <= Math.Min(_width - 1, start.X + radius); x++)
        {
            var index = y * _width + x;
            if (!_walkable[index]) continue;
            var distance = Vector2.Distance(source, ToWorld(new Cell(x, y)));
            if (distance >= bestDistance) continue;
            best = index;
            bestDistance = distance;
        }
        if (best < 0 || bestDistance > maxDistance)
        {
            hit = default;
            return false;
        }
        hit = new NavigationHit2D
        {
            position = ToWorld(new Cell(best % _width, best / _width)),
            distance = bestDistance,
            mask = 1
        };
        return true;
    }

    public bool CalculatePath(Vector2 source, Vector2 target, NavigationPath2D path)
    {
        var sourceCell = FindNearest(ToCell(source));
        var targetCell = FindNearest(ToCell(target));
        if (sourceCell is null || targetCell is null)
        {
            path.ClearCorners();
            return false;
        }
        var start = sourceCell.Value;
        var goal = targetCell.Value;
        var open = new PriorityQueue<Cell, (long Cost, int Y, int X)>();
        var cameFrom = new Dictionary<Cell, Cell>();
        var scores = new Dictionary<Cell, Fix64> { [start] = Fix64.Zero };
        open.Enqueue(start, (Heuristic(start, goal).RawValue, start.Y, start.X));
        while (open.TryDequeue(out var current, out _))
        {
            if (current == goal)
            {
                var cells = new List<Cell> { current };
                while (cameFrom.TryGetValue(current, out var previous))
                {
                    current = previous;
                    cells.Add(current);
                }
                cells.Reverse();
                var corners = Simplify(cells.Select(ToWorld).ToArray());
                corners[0] = source;
                corners[^1] = target;
                path.corners = corners;
                path.status = NavigationPathStatus.PathComplete;
                return true;
            }
            foreach (var neighbour in Neighbours(current))
            {
                var diagonal = neighbour.X != current.X && neighbour.Y != current.Y;
                var score = scores[current] +
                            (diagonal ? Fix64.Parse("1.41421356") : Fix64.One) * _cellSize;
                if (scores.TryGetValue(neighbour, out var known) && score >= known) continue;
                scores[neighbour] = score;
                cameFrom[neighbour] = current;
                var priority = score + Heuristic(neighbour, goal);
                open.Enqueue(neighbour, (priority.RawValue, neighbour.Y, neighbour.X));
            }
        }
        path.ClearCorners();
        return false;
    }

    private IEnumerable<Cell> Neighbours(Cell cell)
    {
        for (var y = -1; y <= 1; y++)
        for (var x = -1; x <= 1; x++)
        {
            if (x == 0 && y == 0) continue;
            var value = new Cell(cell.X + x, cell.Y + y);
            if (!InBounds(value) || !_walkable[Index(value)]) continue;
            if (x != 0 && y != 0 &&
                (!_walkable[Index(new Cell(cell.X + x, cell.Y))] ||
                 !_walkable[Index(new Cell(cell.X, cell.Y + y))]))
                continue;
            yield return value;
        }

        var scene = _surface.gameObject.scene;
        if (scene is null) yield break;
        foreach (var link in scene.QueryComponents<NavigationLink2D>())
        {
            if (!IsEnabled(link)) continue;
            var start = FindNearest(ToCell(link.worldStart));
            var end = FindNearest(ToCell(link.worldEnd));
            if (start is null || end is null) continue;
            if (cell == start.Value) yield return end.Value;
            else if (link.bidirectional && cell == end.Value) yield return start.Value;
        }
    }

    private Cell? FindNearest(Cell cell)
    {
        if (InBounds(cell) && _walkable[Index(cell)]) return cell;
        for (var radius = 1; radius <= 16; radius++)
        for (var y = -radius; y <= radius; y++)
        for (var x = -radius; x <= radius; x++)
        {
            var candidate = new Cell(cell.X + x, cell.Y + y);
            if (InBounds(candidate) && _walkable[Index(candidate)]) return candidate;
        }
        return null;
    }

    private Cell ToCell(Vector2 point) => new(
        (int)((point.x - _origin.x) / _cellSize),
        (int)((point.y - _origin.y) / _cellSize));
    private Vector2 ToWorld(Cell cell) => new(
        _origin.x + cell.X * _cellSize,
        _origin.y + cell.Y * _cellSize);
    private int Index(Cell cell) => cell.Y * _width + cell.X;
    private bool InBounds(Cell cell) =>
        cell.X >= 0 && cell.Y >= 0 && cell.X < _width && cell.Y < _height;
    private static Fix64 Heuristic(Cell left, Cell right) =>
        Fix64.Abs(left.X - right.X) + Fix64.Abs(left.Y - right.Y);

    private static Vector2[] Simplify(Vector2[] points)
    {
        if (points.Length <= 2) return points;
        var result = new List<Vector2> { points[0] };
        var previousDirection = (points[1] - points[0]).normalized;
        for (var index = 2; index < points.Length; index++)
        {
            var direction = (points[index] - points[index - 1]).normalized;
            if (direction != previousDirection) result.Add(points[index - 1]);
            previousDirection = direction;
        }
        result.Add(points[^1]);
        return [.. result];
    }

    private static bool ExpandedContains(Bounds2D bounds, Vector2 point, Fix64 padding) =>
        point.x >= bounds.min.x - padding && point.x <= bounds.max.x + padding &&
        point.y >= bounds.min.y - padding && point.y <= bounds.max.y + padding;
    private static bool IsEnabled(Component item) => item.enabled && item.gameObject.activeInHierarchy;
    private readonly record struct Cell(int X, int Y);
}
