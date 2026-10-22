namespace BEngine.Physics2D;

/// <summary>Sweep-and-prune candidates, sorted back into the original stable collider order.</summary>
internal sealed partial class PhysicsBroadphase2D
{
    private static readonly Comparison<Proxy> ProxyComparison = CompareProxies;
    private static readonly Comparison<(int Left, int Right)> PairComparison = ComparePairs;
    private readonly List<Proxy> _proxies = [];
    private readonly List<(int Left, int Right)> _pairs = [];
    internal IReadOnlyList<(int Left, int Right)> Calculate(IReadOnlyList<Collider2D> colliders,
        IReadOnlyDictionary<Collider2D, PhysicsShape2D> shapes)
    {
        _proxies.Clear(); _pairs.Clear();
        for (var index = 0; index < colliders.Count; index++) _proxies.Add(new(index, shapes[colliders[index]].Bounds));
        _proxies.Sort(ProxyComparison);
        for (var leftIndex = 0; leftIndex < _proxies.Count; leftIndex++)
        {
            var left = _proxies[leftIndex];
            for (var rightIndex = leftIndex + 1; rightIndex < _proxies.Count; rightIndex++)
            {
                var right = _proxies[rightIndex];
                if (right.Bounds.Min.x > left.Bounds.Max.x) break;
                if (!left.Bounds.Intersects(right.Bounds)) continue;
                _pairs.Add(left.Index < right.Index ? (left.Index, right.Index) : (right.Index, left.Index));
            }
        }
        _pairs.Sort(PairComparison);
        return _pairs;
    }
    private static int CompareProxies(Proxy left, Proxy right)
    { var order = left.Bounds.Min.x.CompareTo(right.Bounds.Min.x); return order != 0 ? order : left.Index.CompareTo(right.Index); }
    private static int ComparePairs((int Left, int Right) left, (int Left, int Right) right)
    { var order = left.Left.CompareTo(right.Left); return order != 0 ? order : left.Right.CompareTo(right.Right); }
    private readonly record struct Proxy(int Index, PhysicsBounds2D Bounds);
}
