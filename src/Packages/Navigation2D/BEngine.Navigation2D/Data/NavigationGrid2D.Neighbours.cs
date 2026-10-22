namespace BEngine.Navigation2D;

internal sealed partial class NavigationGrid2D
{
    // Search retains this value enumerator across its cooperative yields.
    // Each expanded cell previously allocated a separate iterator object.
    private readonly struct NeighbourEnumerable(NavigationGrid2D grid, Cell cell, ulong areaMask,
        List<LinkEdge> links, Vector2 axisX, Vector2 axisY)
    {
        public NeighbourEnumerator GetEnumerator() => new(grid, cell, areaMask, links, axisX, axisY);
    }

    private struct NeighbourEnumerator(NavigationGrid2D grid, Cell cell, ulong areaMask,
        List<LinkEdge> links, Vector2 axisX, Vector2 axisY) : IDisposable
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
                Current = (value, (axisX * _x + axisY * _y).magnitude);
                return true;
            }
            // Construct at the original foreach boundary: edits to the link
            // list during grid neighbours remain visible; edits after this
            // point retain List<T>.Enumerator's fail-fast behavior.
            if (!_linksStarted) { _linkEnumerator = links.GetEnumerator(); _linksStarted = true; }
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
}
