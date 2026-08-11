using BEngine;
using BEngine.Terrain;

namespace BEngine.Navigation;

public static class NavMesh
{
    public const int AllAreas = -1;
    private static Scene? _scene;

    internal static void SetScene(Scene? scene) => _scene = scene;

    public static bool CalculatePath(Vector3 sourcePosition, Vector3 targetPosition, int areaMask, NavMeshPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_scene is null) { path.ClearCorners(); return false; }
        foreach (var surface in _scene.gameObjects.SelectMany(item => item.GetComponents<NavMeshSurface>())
                     .Where(item => item.enabled && item.hasData).OrderBy(item => item.Id))
        {
            if (surface.CalculatePath(sourcePosition, targetPosition, path)) return true;
        }
        path.ClearCorners();
        return false;
    }

    public static bool SamplePosition(Vector3 sourcePosition, out NavMeshHit hit, Fix64 maxDistance, int areaMask)
    {
        if (_scene is not null)
        {
            foreach (var surface in _scene.gameObjects.SelectMany(item => item.GetComponents<NavMeshSurface>())
                         .Where(item => item.enabled && item.hasData).OrderBy(item => item.Id))
                if (surface.Sample(sourcePosition, maxDistance, out hit)) return true;
        }
        hit = default;
        return false;
    }
}

internal sealed class NavigationRuntimeSystem : ISceneRuntimeSystem
{
    public int order => 100;
    public string packageId => "com.bengine.navigation";
    public void Start(Scene scene) => NavMesh.SetScene(scene);
    public void Update(Scene scene, Fix64 deltaTime)
    {
        NavMesh.SetScene(scene);
        foreach (var agent in scene.gameObjects.Where(item => item.activeInHierarchy)
                     .SelectMany(item => item.GetComponents<NavMeshAgent>()).OrderBy(item => item.Id)) agent.Tick(deltaTime);
    }
    public void Stop(Scene scene) => NavMesh.SetScene(null);
}

internal sealed class NavMeshGrid
{
    private readonly NavMeshSurface _surface;
    private readonly int _width;
    private readonly int _height;
    private readonly bool[] _walkable;
    private readonly Fix64[] _heights;
    private readonly Vector3 _origin;
    private readonly Fix64 _cellSize;

    private NavMeshGrid(NavMeshSurface surface, int width, int height, Vector3 origin, Fix64 cellSize)
    {
        _surface = surface; _width = width; _height = height; _origin = origin; _cellSize = cellSize;
        _walkable = new bool[width * height]; _heights = new Fix64[width * height];
    }

    public static NavMeshGrid Bake(NavMeshSurface surface)
    {
        var cell = Mathf.Max(surface.voxelSize, Fix64.Parse("0.1"));
        var width = Math.Clamp((int)(surface.size.x / cell) + 1, 2, 512);
        var height = Math.Clamp((int)(surface.size.z / cell) + 1, 2, 512);
        var origin = surface.transform.TransformPoint(surface.center - surface.size / 2);
        var grid = new NavMeshGrid(surface, width, height, origin, cell);
        var scene = surface.gameObject.scene;
        var obstacles = scene?.gameObjects.Where(item => item.activeInHierarchy)
            .SelectMany(item => item.GetComponents<NavMeshObstacle>()).Where(item => item.enabled && item.carving).ToArray() ?? [];
        var volumes = scene?.gameObjects.Where(item => item.activeInHierarchy)
            .SelectMany(item => item.GetComponents<NavMeshModifierVolume>()).Where(item => item.enabled).ToArray() ?? [];
        var modifiers = scene?.gameObjects.Where(item => item.activeInHierarchy)
            .SelectMany(item => item.GetComponents<NavMeshModifier>()).Where(item => item.enabled).ToArray() ?? [];
        var terrain = scene?.gameObjects.Where(item => item.activeInHierarchy)
            .SelectMany(item => item.GetComponents<BEngine.Terrain.Terrain>()).FirstOrDefault(item => item.enabled);
        for (var z = 0; z < height; z++)
        for (var x = 0; x < width; x++)
        {
            var point = new Vector3(origin.x + x * cell, origin.y, origin.z + z * cell);
            var walkable = !obstacles.Any(item => item.Contains(point, surface.agentRadius)) &&
                           !volumes.Any(item => item.area == 1 && item.Contains(point)) &&
                           !modifiers.Any(item => item.Blocks(point, surface.agentTypeID));
            if (terrain is not null)
            {
                point = new Vector3(point.x, terrain.SampleHeight(point), point.z);
                var data = terrain.GetTerrainData();
                if (data is not null)
                {
                    var local = terrain.transform.InverseTransformPoint(point);
                    var u = data.size.x == 0 ? Fix64.Zero : local.x / data.size.x;
                    var v = data.size.z == 0 ? Fix64.Zero : local.z / data.size.z;
                    walkable &= data.GetSteepness(u, v) <= surface.agentSlope;
                }
            }
            var index = z * width + x;
            grid._walkable[index] = walkable;
            grid._heights[index] = point.y;
        }
        return grid;
    }

    public bool Sample(Vector3 source, Fix64 maxDistance, out NavMeshHit hit)
    {
        var start = ToCell(source);
        var radius = Math.Max(1, (int)(maxDistance / _cellSize));
        var best = -1;
        var bestDistance = maxDistance + Fix64.One;
        for (var z = Math.Max(0, start.Z - radius); z <= Math.Min(_height - 1, start.Z + radius); z++)
        for (var x = Math.Max(0, start.X - radius); x <= Math.Min(_width - 1, start.X + radius); x++)
        {
            var index = z * _width + x;
            if (!_walkable[index]) continue;
            var distance = Vector3.Distance(source, ToWorld(new Cell(x, z)));
            if (distance < bestDistance) { best = index; bestDistance = distance; }
        }
        if (best < 0 || bestDistance > maxDistance) { hit = default; return false; }
        hit = new NavMeshHit { position = ToWorld(new Cell(best % _width, best / _width)), distance = bestDistance, mask = 1 };
        return true;
    }

    public bool CalculatePath(Vector3 source, Vector3 target, NavMeshPath path)
    {
        var sourceCell = FindNearest(ToCell(source));
        var targetCell = FindNearest(ToCell(target));
        if (sourceCell is null || targetCell is null) { path.ClearCorners(); return false; }
        var start = sourceCell.Value;
        var goal = targetCell.Value;
        var open = new PriorityQueue<Cell, (long, int, int)>();
        var cameFrom = new Dictionary<Cell, Cell>();
        var scores = new Dictionary<Cell, Fix64> { [start] = Fix64.Zero };
        open.Enqueue(start, (Heuristic(start, goal).RawValue, start.Z, start.X));
        while (open.TryDequeue(out var current, out _))
        {
            if (current == goal)
            {
                var cells = new List<Cell> { current };
                while (cameFrom.TryGetValue(current, out var previous)) { current = previous; cells.Add(current); }
                cells.Reverse();
                var corners = Simplify(cells.Select(ToWorld).ToArray());
                corners[0] = source;
                corners[^1] = target;
                path.corners = corners;
                path.status = NavMeshPathStatus.PathComplete;
                return true;
            }
            foreach (var neighbour in Neighbours(current))
            {
                var vertical = Mathf.Abs(_heights[Index(neighbour)] - _heights[Index(current)]);
                if (vertical > _surface.agentClimb) continue;
                var score = scores[current] + (neighbour.X != current.X && neighbour.Z != current.Z
                    ? Fix64.Parse("1.41421356") : Fix64.One) * _cellSize;
                if (scores.TryGetValue(neighbour, out var known) && score >= known) continue;
                scores[neighbour] = score;
                cameFrom[neighbour] = current;
                var priority = score + Heuristic(neighbour, goal);
                open.Enqueue(neighbour, (priority.RawValue, neighbour.Z, neighbour.X));
            }
        }
        path.ClearCorners();
        return false;
    }

    private IEnumerable<Cell> Neighbours(Cell cell)
    {
        for (var z = -1; z <= 1; z++)
        for (var x = -1; x <= 1; x++)
        {
            if (x == 0 && z == 0) continue;
            var value = new Cell(cell.X + x, cell.Z + z);
            if (InBounds(value) && _walkable[Index(value)]) yield return value;
        }
        IEnumerable<NavMeshLink> links = _surface.gameObject.scene?.gameObjects.Where(item => item.activeInHierarchy)
            .SelectMany(item => item.GetComponents<NavMeshLink>()).Where(item => item.enabled)
            .OrderBy(item => item.Id) ?? Enumerable.Empty<NavMeshLink>();
        foreach (var link in links)
        {
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
        for (var radius = 1; radius <= 8; radius++)
        for (var z = -radius; z <= radius; z++)
        for (var x = -radius; x <= radius; x++)
        {
            var candidate = new Cell(cell.X + x, cell.Z + z);
            if (InBounds(candidate) && _walkable[Index(candidate)]) return candidate;
        }
        return null;
    }
    private Cell ToCell(Vector3 point) => new((int)((point.x - _origin.x) / _cellSize), (int)((point.z - _origin.z) / _cellSize));
    private Vector3 ToWorld(Cell cell) => new(_origin.x + cell.X * _cellSize, _heights[Index(cell)], _origin.z + cell.Z * _cellSize);
    private int Index(Cell cell) => cell.Z * _width + cell.X;
    private bool InBounds(Cell cell) => cell.X >= 0 && cell.Z >= 0 && cell.X < _width && cell.Z < _height;
    private static Fix64 Heuristic(Cell a, Cell b) => Fix64.Abs(a.X - b.X) + Fix64.Abs(a.Z - b.Z);
    private static Vector3[] Simplify(Vector3[] points)
    {
        if (points.Length <= 2) return points;
        var result = new List<Vector3> { points[0] };
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
    private readonly record struct Cell(int X, int Z);
}
