using System.Buffers;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public readonly struct UQueryBuilder<T> where T : VisualElement
{
    private readonly VisualElement _root;
    private readonly string? _name;
    private readonly string? _className;

    internal UQueryBuilder(VisualElement root, string? name, string? className)
    {
        _root = root;
        _name = name;
        _className = className;
    }

    public T? First() => FirstOrDefault();

    public T? FirstOrDefault()
    {
        if (Match(_root) is { } rootMatch) return rootMatch;
        if (_root.Children.Count == 0) return null;
        var pending = ArrayPool<VisualElement>.Shared.Rent(Math.Max(32, _root.Children.Count));
        var count = 0;
        var current = _root;
        try
        {
            while (true)
            {
                var children = current.ChildrenSpanUnchecked;
                if (children.Length > pending.Length - count)
                {
                    var larger = ArrayPool<VisualElement>.Shared.Rent(count + children.Length);
                    Array.Copy(pending, larger, count);
                    ArrayPool<VisualElement>.Shared.Return(pending, clearArray: true);
                    pending = larger;
                }
                for (var index = children.Length - 1; index >= 0; index--)
                    pending[count++] = children[index];
                if (count == 0) return null;
                current = pending[--count];
                pending[count] = null!;
                if (Match(current) is { } match) return match;
            }
        }
        finally { ArrayPool<VisualElement>.Shared.Return(pending, clearArray: true); }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private T? Match(VisualElement element) => element is T typed &&
        (_name is null || element.name.Equals(_name, StringComparison.Ordinal)) &&
        (_className is null || element.ClassListContains(_className)) ? typed : null;

    public List<T> ToList()
    {
        var result = new List<T>();
        foreach (var element in this) result.Add(element);
        return result;
    }

    public void ForEach(Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        foreach (var element in this) action(element);
    }

    public Enumerator GetEnumerator() => new(_root, _name, _className);

    public struct Enumerator
    {
        private readonly string? _name;
        private readonly string? _className;
        private readonly Stack<VisualElement> _pending;

        internal Enumerator(VisualElement root, string? name, string? className)
        {
            _name = name;
            _className = className;
            _pending = new Stack<VisualElement>();
            _pending.Push(root);
            Current = null!;
        }

        public T Current { get; private set; }

        public bool MoveNext()
        {
            while (_pending.Count > 0)
            {
                var candidate = _pending.Pop();
                var children = candidate.ChildrenSpanUnchecked;
                for (var index = children.Length - 1; index >= 0; index--)
                    _pending.Push(children[index]);
                if (candidate is not T typed ||
                    _name is not null && !candidate.name.Equals(_name, StringComparison.Ordinal) ||
                    _className is not null && !candidate.ClassListContains(_className)) continue;
                Current = typed;
                return true;
            }
            return false;
        }
    }
}
