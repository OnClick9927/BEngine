namespace BEngine.Navigation2D;

internal sealed partial class NavigationGrid2D
{
    // Search retains this value enumerator across its cooperative yields.
    // Each expanded cell previously allocated a separate iterator object.
    private readonly struct NeighbourEnumerable(NavigationGrid2D grid, Cell cell, ulong areaMask,
        SearchWorkspace workspace)
    {
        public NeighbourEnumerator GetEnumerator() => new(grid, cell, areaMask, workspace);
    }

    private struct NeighbourEnumerator(NavigationGrid2D grid, Cell cell, ulong areaMask,
        SearchWorkspace workspace) : IDisposable
    {
        private int _x = -2, _y = -1;
        private bool _linksStarted, _finished;
        private List<LinkEdge>.Enumerator _linkEnumerator;
        public (Cell Cell, Fix64 Cost) Current { get; private set; }

        public bool MoveNext()
        {
            if (_finished) return false;
            while (_y <= 1)
            {
                if (++_x > 1) { _x = -1; _y++; }
                if (_y > 1) break;
                if (_x == 0 && _y == 0) continue;
                var value = new Cell(cell.X + _x, cell.Y + _y);
                if (!grid.IsWalkable(value, areaMask)) continue;
                if (_x != 0 && _y != 0 &&
                    (!grid.IsWalkable(new Cell(cell.X + _x, cell.Y), areaMask) ||
                     !grid.IsWalkable(new Cell(cell.X, cell.Y + _y), areaMask))) continue;
                Current = (value, workspace.Costs.Get(_x, _y));
                return true;
            }
            // Construct at the original foreach boundary: edits to the link
            // list during grid neighbours remain visible; edits after this
            // point retain List<T>.Enumerator's fail-fast behavior.
            if (!_linksStarted) { _linkEnumerator = workspace.Links.GetEnumerator(); _linksStarted = true; }
            while (_linkEnumerator.MoveNext())
            {
                var link = _linkEnumerator.Current;
                if (cell != link.Start) continue;
                Current = (link.End, link.Cost);
                return true;
            }
            _finished = true;
            return false;
        }

        public void Dispose()
        {
            if (_linksStarted) _linkEnumerator.Dispose();
            _finished = true;
        }
    }

    // The axes are fixed for one search. Evaluate the original expression once
    // per encountered direction, preserving checked arithmetic for blocked edges.
    private struct NeighbourCosts(Vector2 axisX, Vector2 axisY)
    {
        private readonly Vector2 _axisX = axisX, _axisY = axisY;
        private Fix64 _bottomLeft, _bottom, _bottomRight, _left, _right, _topLeft, _top, _topRight;
        private byte _prepared;

        internal Fix64 Get(int x, int y) => (x, y) switch
        {
            (-1, -1) => Prepare(ref _bottomLeft, 1, x, y), (0, -1) => Prepare(ref _bottom, 2, x, y),
            (1, -1) => Prepare(ref _bottomRight, 4, x, y), (-1, 0) => Prepare(ref _left, 8, x, y),
            (1, 0) => Prepare(ref _right, 16, x, y), (-1, 1) => Prepare(ref _topLeft, 32, x, y),
            (0, 1) => Prepare(ref _top, 64, x, y), (1, 1) => Prepare(ref _topRight, 128, x, y),
            _ => throw new ArgumentOutOfRangeException(nameof(x))
        };

        private Fix64 Prepare(ref Fix64 cost, byte bit, int x, int y)
        {
            if ((_prepared & bit) == 0)
            {
                cost = (_axisX * x + _axisY * y).magnitude;
                _prepared |= bit;
            }
            return cost;
        }
    }
}
