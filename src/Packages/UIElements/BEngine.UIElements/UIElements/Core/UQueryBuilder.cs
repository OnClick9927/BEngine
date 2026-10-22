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
        foreach (var element in this) return element;
        return null;
    }

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
                for (var index = candidate.Children.Count - 1; index >= 0; index--)
                    _pending.Push(candidate.Children[index]);
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
