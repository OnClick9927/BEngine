using BEngine.Physics2D;

namespace BEngine.Navigation2D;

internal sealed partial class NavigationGrid2D
{
    private readonly NavigationSurface2D _surface;
    private readonly int _width;
    private readonly int _height;
    private readonly bool[] _walkable;
    private readonly byte[] _areas;
    private readonly Vector2 _localOrigin;
    private readonly Fix64 _cellSize;
    private readonly Stack<SearchWorkspace> _searchWorkspaces = [];
    private const int MaximumRetainedWorkspaces = 4;
    private const int MaximumRetainedSearchCapacity = 8192;

    private NavigationGrid2D(
        NavigationSurface2D surface, int width, int height, Vector2 localOrigin, Fix64 cellSize)
    {
        _surface = surface;
        _width = width;
        _height = height;
        _localOrigin = localOrigin;
        _cellSize = cellSize;
        _walkable = new bool[width * height];
        _areas = new byte[width * height];
    }

    public static NavigationGrid2D Bake(NavigationSurface2D surface)
    {
        var cell = Mathf.Max(surface.cellSize, Fix64.Parse("0.1"));
        var size = new Vector2(Mathf.Abs(surface.size.x), Mathf.Abs(surface.size.y));
        var width = Math.Clamp((int)(size.x / cell) + 1, 2, 1024);
        var height = Math.Clamp((int)(size.y / cell) + 1, 2, 1024);
        var origin = surface.center - size / 2;
        var grid = new NavigationGrid2D(surface, width, height, origin, cell);
        var scene = surface.gameObject.scene;
        var obstacles = scene?.QueryComponents<NavigationObstacle2D>().ToArray()
            .Where(IsEnabled).Where(item => item.carving && Collects(surface, item)).ToArray() ?? [];
        var volumes = scene?.QueryComponents<NavigationModifierVolume2D>().ToArray()
            .Where(IsEnabled).Where(item => Collects(surface, item)).ToArray() ?? [];
        var modifiers = scene?.QueryComponents<NavigationModifier2D>().ToArray()
            .Where(IsEnabled).Where(item => Collects(surface, item)).ToArray() ?? [];
        var colliders = surface.useGeometry == NavigationCollectGeometry2D.PhysicsColliders
            ? scene?.QueryComponents<Collider2D>().ToArray()
                .Where(IsEnabled)
                .Where(item => !item.isTrigger &&
                               (surface.layerMask & SortingLayer.ToMask(item.gameObject.layer)) != 0 &&
                               Collects(surface, item))
                .ToArray() ?? []
            : [];
        var sprites = surface.useGeometry == NavigationCollectGeometry2D.Sprites
            ? scene?.QueryComponents<SpriteRenderer>().ToArray()
                .Where(IsEnabled)
                .Where(item =>
                    (surface.layerMask & SortingLayer.ToMask(item.gameObject.layer)) != 0 &&
                    Collects(surface, item))
                .ToArray() ?? []
            : [];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var point = grid.ToWorld(new Cell(x, y));
            var walkable = !obstacles.Any(item => item.Contains(point, surface.agentRadius)) &&
                           !volumes.Any(item => item.area == 1 && item.Contains(point)) &&
                           !modifiers.Any(item => item.Blocks(point, surface.agentTypeId)) &&
                           !colliders.Any(item => ExpandedContains(item.bounds, point, surface.agentRadius)) &&
                           !sprites.Any(item => SpriteContains(item, point, surface.agentRadius));
            grid._walkable[y * width + x] = walkable;
            var area = 0;
            foreach (var volume in volumes)
                if (volume.Contains(point)) area = Math.Clamp(volume.area, 0, 63);
            foreach (var modifier in modifiers)
                if (modifier.TryGetArea(point, surface.agentTypeId, out var modified)) area = modified;
            grid._areas[y * width + x] = (byte)area;
        }
        NavigationMethodPreparation.Prepare();
        return grid;
    }

    public bool Sample(Vector2 source, Fix64 maxDistance, out NavigationHit2D hit, ulong areaMask)
    {
        var start = ToCell(source);
        var worldCellSize = Mathf.Max(Fix64.Epsilon, Mathf.Min(
            Vector2.Distance(ToWorld(new Cell(0, 0)), ToWorld(new Cell(1, 0))),
            Vector2.Distance(ToWorld(new Cell(0, 0)), ToWorld(new Cell(0, 1)))));
        var radius = Math.Max(1, (int)(maxDistance / worldCellSize) + 1);
        var best = -1;
        var bestDistance = maxDistance + Fix64.One;
        for (var y = Math.Max(0, start.Y - radius); y <= Math.Min(_height - 1, start.Y + radius); y++)
        for (var x = Math.Max(0, start.X - radius); x <= Math.Min(_width - 1, start.X + radius); x++)
        {
            var index = y * _width + x;
            if (!_walkable[index] || (areaMask & (1UL << _areas[index])) == 0) continue;
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
            mask = 1UL << _areas[best]
        };
        return true;
    }

    public bool CalculatePath(Vector2 source, Vector2 target, NavigationPath2D path, ulong areaMask)
    {
        var result = false;
        foreach (var step in Search(source, target, path, areaMask)) result = step;
        return result;
    }

    internal IEnumerator<bool> SearchSteps(Vector2 source, Vector2 target, NavigationPath2D path, ulong areaMask)
        => Search(source, target, path, areaMask).GetEnumerator();

    private IEnumerable<bool> Search(Vector2 source, Vector2 target, NavigationPath2D path, ulong areaMask)
    {
        var origin = ToWorld(new Cell(0, 0));
        var axisX = ToWorld(new Cell(1, 0)) - origin;
        var axisY = ToWorld(new Cell(0, 1)) - origin;
        Vector2 World(Cell cell) => origin + axisX * cell.X + axisY * cell.Y;
        Fix64 Estimate(Cell left, Cell right) => Vector2.Distance(World(left), World(right));
        var sourceCell = FindNearest(ToCell(source), areaMask);
        var targetCell = FindNearest(ToCell(target), areaMask);
        if (sourceCell is null || targetCell is null)
        {
            path.ClearCorners();
            yield return false; yield break;
        }
        var start = sourceCell.Value;
        var goal = targetCell.Value;
        var workspace = _searchWorkspaces.TryPop(out var available) ? available : new SearchWorkspace();
        try
        {
        var links = workspace.Links;
        if (_surface.gameObject.scene is { } scene)
            foreach (var link in scene.QueryComponents<NavigationLink2D>())
            {
                if (!IsEnabled(link) || !Collects(_surface, link) ||
                    (areaMask & (1UL << Math.Clamp(link.area, 0, 63))) == 0) continue;
                var linkStart = FindNearest(ToCell(link.worldStart), areaMask);
                var linkEnd = FindNearest(ToCell(link.worldEnd), areaMask);
                if (linkStart is null || linkEnd is null) continue;
                var cost = Estimate(linkStart.Value, linkEnd.Value) *
                    (link.costModifier < 0 ? Fix64.One : link.costModifier);
                links.Add(new LinkEdge(linkStart.Value, linkEnd.Value, cost));
                if (link.bidirectional) links.Add(new LinkEdge(linkEnd.Value, linkStart.Value, cost));
            }
        var open = workspace.Open;
        var cameFrom = workspace.CameFrom;
        var scores = workspace.Scores;
        scores[start] = Fix64.Zero;
        var closed = workspace.Closed;
        open.Enqueue(start, (0, start.Y, start.X));
        var expanded = 0;
        while (open.TryDequeue(out var current, out _))
        {
            if (++expanded % 32 == 0) yield return false;
            if (!closed.Add(current)) continue;
            if (current == goal)
            {
                var cells = workspace.Cells;
                cells.Add(current);
                while (cameFrom.TryGetValue(current, out var previous))
                {
                    current = previous;
                    cells.Add(current);
                }
                cells.Reverse();
                var corners = Simplify(cells.Select(World).ToArray());
                corners[0] = source;
                var reachesTarget = IsWalkable(ToCell(target), areaMask) && ToCell(target) == goal;
                corners[^1] = reachesTarget ? target : World(goal);
                path.corners = corners;
                path.status = reachesTarget ? NavigationPathStatus.PathComplete : NavigationPathStatus.PathPartial;
                yield return true; yield break;
            }
            foreach (var edge in Neighbours(current, areaMask, links, axisX, axisY))
            {
                var neighbour = edge.Cell;
                if (closed.Contains(neighbour)) continue;
                var score = scores[current] + edge.Cost;
                if (scores.TryGetValue(neighbour, out var known) && score >= known) continue;
                scores[neighbour] = score;
                cameFrom[neighbour] = current;
                var priority = score + (links.Count == 0 ? Estimate(neighbour, goal) : Fix64.Zero);
                open.Enqueue(neighbour, (priority.RawValue, neighbour.Y, neighbour.X));
            }
        }
        path.ClearCorners();
        yield return false;
        }
        finally
        {
            // An iterator owns its workspace until completion or Dispose, so
            // interleaved searches and cancellation cannot overwrite each other.
            var retain = workspace.CanRetain && _searchWorkspaces.Count < MaximumRetainedWorkspaces;
            workspace.Clear();
            if (retain) _searchWorkspaces.Push(workspace);
        }
    }

    private sealed class SearchWorkspace
    {
        internal readonly List<LinkEdge> Links = [];
        internal readonly PriorityQueue<Cell, (long Cost, int Y, int X)> Open = new();
        internal readonly Dictionary<Cell, Cell> CameFrom = [];
        internal readonly Dictionary<Cell, Fix64> Scores = [];
        internal readonly HashSet<Cell> Closed = [];
        internal readonly List<Cell> Cells = [];
        internal bool CanRetain => Links.Capacity <= MaximumRetainedSearchCapacity &&
            Open.EnsureCapacity(0) <= MaximumRetainedSearchCapacity &&
            CameFrom.EnsureCapacity(0) <= MaximumRetainedSearchCapacity &&
            Scores.EnsureCapacity(0) <= MaximumRetainedSearchCapacity &&
            Closed.EnsureCapacity(0) <= MaximumRetainedSearchCapacity &&
            Cells.Capacity <= MaximumRetainedSearchCapacity;
        internal void Clear()
        { Links.Clear(); Open.Clear(); CameFrom.Clear(); Scores.Clear(); Closed.Clear(); Cells.Clear(); }
    }

    private NeighbourEnumerable Neighbours(Cell cell, ulong areaMask, List<LinkEdge> links, Vector2 axisX, Vector2 axisY)
        => new(this, cell, areaMask, links, axisX, axisY);

    private Cell? FindNearest(Cell cell, ulong areaMask)
    {
        if (IsWalkable(cell, areaMask)) return cell;
        for (var radius = 1; radius <= 16; radius++)
        for (var y = -radius; y <= radius; y++)
        for (var x = -radius; x <= radius; x++)
        {
            var candidate = new Cell(cell.X + x, cell.Y + y);
            if (IsWalkable(candidate, areaMask)) return candidate;
        }
        return null;
    }

    private Cell ToCell(Vector2 point)
    {
        var local = _surface.transform.InverseTransformPoint(point);
        return new Cell(
            (int)((local.x - _localOrigin.x) / _cellSize),
            (int)((local.y - _localOrigin.y) / _cellSize));
    }
    private Vector2 ToWorld(Cell cell) => _surface.transform.TransformPoint(new Vector2(
        _localOrigin.x + cell.X * _cellSize,
        _localOrigin.y + cell.Y * _cellSize));
    private int Index(Cell cell) => cell.Y * _width + cell.X;
    private bool InBounds(Cell cell) =>
        cell.X >= 0 && cell.Y >= 0 && cell.X < _width && cell.Y < _height;
    private bool IsWalkable(Cell cell, ulong areaMask) => InBounds(cell) &&
        _walkable[Index(cell)] && (areaMask & (1UL << _areas[Index(cell)])) != 0;
    private Fix64 Heuristic(Cell left, Cell right)
    {
        var x = Math.Abs(left.X - right.X);
        var y = Math.Abs(left.Y - right.Y);
        return Vector2.Distance(ToWorld(left), ToWorld(right));
    }

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

    private static bool SpriteContains(SpriteRenderer renderer, Vector2 point, Fix64 padding)
    {
        var pivot = renderer.useSpritePivot && renderer.sprite is not null
            ? renderer.sprite.pivot : renderer.pivot;
        var size = new Vector2(Mathf.Abs(renderer.size.x), Mathf.Abs(renderer.size.y));
        var localCenter = new Vector2(
            (Fix64.Half - pivot.x) * size.x,
            (Fix64.Half - pivot.y) * size.y);
        var scale = renderer.transform.lossyScale;
        var localPadding = new Vector2(
            padding / Mathf.Max(Mathf.Abs(scale.x), Fix64.Epsilon),
            padding / Mathf.Max(Mathf.Abs(scale.y), Fix64.Epsilon));
        var local = renderer.transform.InverseTransformPoint(point) - localCenter;
        return Mathf.Abs(local.x) <= size.x / 2 + localPadding.x &&
               Mathf.Abs(local.y) <= size.y / 2 + localPadding.y;
    }

    private static bool Collects(NavigationSurface2D surface, Component component)
    {
        if (surface.collectObjects == CollectObjects.All) return true;
        if (surface.collectObjects == CollectObjects.Children)
            return ReferenceEquals(component.gameObject, surface.gameObject) ||
                   component.transform.IsChildOf(surface.transform);

        var surfaceBounds = WorldBounds(surface.transform, surface.center, surface.size);
        var componentBounds = component switch
        {
            Collider2D collider => collider.bounds,
            NavigationObstacle2D obstacle => WorldBounds(obstacle.transform, obstacle.center,
                obstacle.shape == NavigationObstacleShape2D.Circle
                    ? Vector2.one * (obstacle.radius * 2) : obstacle.size),
            NavigationModifierVolume2D volume => WorldBounds(volume.transform, volume.center, volume.size),
            NavigationModifier2D modifier when modifier.GetComponent<Collider2D>() is { } collider =>
                collider.bounds,
            NavigationModifier2D modifier => WorldBounds(modifier.transform, Vector2.zero, Vector2.one),
            SpriteRenderer sprite => WorldBounds(sprite.transform,
                new Vector2(
                    (Fix64.Half - (sprite.useSpritePivot && sprite.sprite is not null
                        ? sprite.sprite.pivot.x : sprite.pivot.x)) * Mathf.Abs(sprite.size.x),
                    (Fix64.Half - (sprite.useSpritePivot && sprite.sprite is not null
                        ? sprite.sprite.pivot.y : sprite.pivot.y)) * Mathf.Abs(sprite.size.y)),
                new Vector2(Mathf.Abs(sprite.size.x), Mathf.Abs(sprite.size.y))),
            NavigationLink2D link => SegmentBounds(link.worldStart, link.worldEnd),
            _ => new Bounds2D(component.transform.position, Vector2.zero)
        };
        return Intersects(surfaceBounds, componentBounds);
    }

    private static Bounds2D WorldBounds(Transform transform, Vector2 center, Vector2 size)
    {
        var half = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y)) / 2;
        var first = transform.TransformPoint(center - half);
        var minimum = first;
        var maximum = first;
        foreach (var corner in new[]
                 {
                     new Vector2(center.x - half.x, center.y + half.y),
                     new Vector2(center.x + half.x, center.y - half.y),
                     center + half
                 })
        {
            var world = transform.TransformPoint(corner);
            minimum = Vector2.Min(minimum, world);
            maximum = Vector2.Max(maximum, world);
        }
        return new Bounds2D((minimum + maximum) / 2, maximum - minimum);
    }

    private static Bounds2D SegmentBounds(Vector2 start, Vector2 end)
    {
        var minimum = Vector2.Min(start, end);
        var maximum = Vector2.Max(start, end);
        return new Bounds2D((minimum + maximum) / 2, maximum - minimum);
    }

    private static bool Intersects(Bounds2D left, Bounds2D right) =>
        left.min.x <= right.max.x && left.max.x >= right.min.x &&
        left.min.y <= right.max.y && left.max.y >= right.min.y;

    private static bool IsEnabled(Component item) => item.enabled && item.gameObject.activeInHierarchy;
    private readonly record struct Cell(int X, int Y);
    private readonly record struct LinkEdge(Cell Start, Cell End, Fix64 Cost);
}
