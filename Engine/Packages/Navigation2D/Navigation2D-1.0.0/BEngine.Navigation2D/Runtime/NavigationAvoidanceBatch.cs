namespace BEngine.Navigation2D;

// Dense numeric state and a reusable spatial hash. Candidate order follows the scene query;
// moving each proxy after its Tick preserves the existing sequential fixed-point simulation.
internal sealed class NavigationAvoidanceBatch
{
    // Vector2.magnitude truncates both squared components before taking the square root.
    // Expand spatial/AABB bounds conservatively so tiny radii keep the same neighbors.
    private static readonly long BoundsPaddingRaw = Fix64.Sqrt(Fix64.FromRaw(2)).RawValue + 1;
    private struct Entry { internal Vector2 Position; internal Fix64 Radius; internal bool Active; }
    private readonly FrameDataBuffer<Entry> _entries = new();
    private readonly Dictionary<(long X, long Y), int> _heads = [];
    private readonly List<int> _candidates = [];
    private int[] _next = [], _previous = [];
    private (long X, long Y)[] _cells = [];
    private Fix64 _maximumRadius;
    private long _cellWidth;
    private IReadOnlyList<NavigationAgent2D>? _agents;
    private bool _neighborsInitialized;
    internal long CandidateCount { get; private set; }

    internal void Begin(IReadOnlyList<NavigationAgent2D> agents)
    {
        Clear(); CandidateCount = 0; _maximumRadius = 0; _agents = agents; _neighborsInitialized = false;
        _entries.EnsureCapacity(agents.Count);
        if (_next.Length < agents.Count)
        {
            var capacity = _entries.Capacity;
            _next = new int[capacity]; _previous = new int[capacity]; _cells = new (long, long)[capacity];
        }
        for (var i = 0; i < agents.Count; i++)
        {
            var agent = agents[i];
            var active = !agent.isDestroyed && agent.enabled && agent.gameObject.activeInHierarchy;
            var radius = Fix64.Max(0, agent.radius);
            _entries.Add(new Entry { Position = active ? agent.PeekAvoidancePosition() : default, Radius = radius, Active = active });
            if (active) _maximumRadius = Fix64.Max(_maximumRadius, radius);
        }
        _cellWidth = Math.Max(Fix64.One.RawValue, (long)Int128.Min(long.MaxValue, (Int128)_maximumRadius.RawValue * 2));
        for (var i = 0; i < _entries.Count; i++)
        {
            _next[i] = _previous[i] = -1;
            if (_entries[i].Active) Insert(i, Cell(_entries[i].Position));
        }
    }
    internal void Clear() { _entries.Clear(); _heads.Clear(); _candidates.Clear(); _agents = null; }

    internal void Move(int index, Vector2 position)
    {
        ref var entry = ref _entries[index];
        if (!entry.Active) return;
        entry.Position = position;
        var cell = Cell(position);
        if (cell == _cells[index]) return;
        var previous = _previous[index]; var next = _next[index]; var old = _cells[index];
        if (previous >= 0) _next[previous] = next;
        else if (next >= 0) _heads[old] = next;
        else _heads.Remove(old);
        if (next >= 0) _previous[next] = previous;
        Insert(index, cell);
    }
    internal Vector2 Adjustment(int self, Vector2 position, Fix64 radius, Fix64 speed)
    {
        if (!_neighborsInitialized)
        {
            // The previous all-agent scan initialized every active neighbor lazily at
            // the first avoidance query. Preserve that timing, including parent motion.
            for (var i = 0; i < _entries.Count; i++)
                if (i != self && _entries[i].Active) Move(i, _agents![i].nextPosition);
            _neighborsInitialized = true;
        }
        _candidates.Clear();
        var reach = (Int128)Fix64.Max(0, radius).RawValue + _maximumRadius.RawValue + BoundsPaddingRaw;
        var minX = Coordinate(Clamp((Int128)position.x.RawValue - reach));
        var maxX = Coordinate(Clamp((Int128)position.x.RawValue + reach));
        var minY = Coordinate(Clamp((Int128)position.y.RawValue - reach));
        var maxY = Coordinate(Clamp((Int128)position.y.RawValue + reach));
        // Saturated/extreme ranges and dense crowds use an ordered linear scan, avoiding
        // an expensive candidate sort without imposing an artificial neighbor limit.
        if ((Int128)maxX - minX > 4 || (Int128)maxY - minY > 4) return Linear();
        for (var y = minY; ; y++)
        {
            for (var x = minX; ; x++)
            {
                if (_heads.TryGetValue((x, y), out var head))
                    for (var index = head; index >= 0; index = _next[index]) _candidates.Add(index);
                if (x == maxX) break;
            }
            if (y == maxY) break;
        }
        if (_candidates.Count > _entries.Count / 2) return Linear();
        _candidates.Sort();
        var adjustment = Vector2.zero;
        foreach (var index in _candidates) Accumulate(index, ref adjustment);
        return adjustment;

        Vector2 Linear()
        {
            var result = Vector2.zero;
            for (var i = 0; i < _entries.Count; i++) Accumulate(i, ref result);
            return result;
        }
        void Accumulate(int index, ref Vector2 result)
        {
            if (index == self) return;
            ref var other = ref _entries[index];
            if (!other.Active) return;
            CandidateCount++;
            var separation = position - other.Position;
            var clearance = Fix64.Max(0, radius) + other.Radius;
            // Match Vector2.magnitude's fixed-point rounding while rejecting separated
            // bounding boxes before the comparatively expensive square root.
            var padded = (Int128)clearance.RawValue + BoundsPaddingRaw;
            if (Int128.Abs(separation.x.RawValue) > padded || Int128.Abs(separation.y.RawValue) > padded) return;
            var distance = separation.magnitude;
            if (distance > Fix64.Epsilon && distance < clearance)
                result += separation / distance * (Fix64.One - distance / clearance) * speed;
        }
    }
    private void Insert(int index, (long X, long Y) cell)
    {
        _cells[index] = cell;
        _next[index] = _heads.TryGetValue(cell, out var head) ? head : -1;
        _previous[index] = -1;
        if (_next[index] >= 0) _previous[_next[index]] = index;
        _heads[cell] = index;
    }
    private (long X, long Y) Cell(Vector2 position) => (Coordinate(position.x.RawValue), Coordinate(position.y.RawValue));
    private long Coordinate(long raw) { var value = raw / _cellWidth; return raw % _cellWidth < 0 ? value - 1 : value; }
    private static long Clamp(Int128 value) => (long)Int128.Clamp(value, long.MinValue, long.MaxValue);
}
