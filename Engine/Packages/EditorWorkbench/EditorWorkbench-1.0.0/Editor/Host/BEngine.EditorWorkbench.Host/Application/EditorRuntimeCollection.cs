using System.Collections;

namespace BEngine.Workbench;

/// <summary>Maintains stable dispatch snapshots while callbacks add or remove scenes.
/// The editor owns this collection on its update thread; a snapshot is rebuilt only after a change.</summary>
internal sealed class EditorRuntimeCollection : IReadOnlyList<SceneRuntime>
{
    private readonly List<SceneRuntime> _items = [];
    private SceneRuntime[]? _snapshot;

    public int Count => _items.Count;
    public SceneRuntime this[int index] => _items[index];
    internal SceneRuntime[] Snapshot => _snapshot ??= _items.ToArray();

    internal void Add(SceneRuntime runtime)
    {
        _items.Add(runtime);
        _snapshot = null;
    }

    internal bool Remove(SceneRuntime runtime)
    {
        if (!_items.Remove(runtime)) return false;
        _snapshot = null;
        return true;
    }

    internal int RemoveAll(Predicate<SceneRuntime> predicate)
    {
        // List.RemoveAll can partially move entries if a caller's predicate throws.
        // Invalidate first so a subsequent dispatch never uses a stale snapshot.
        _snapshot = null;
        return _items.RemoveAll(predicate);
    }

    internal void Clear()
    {
        _items.Clear();
        _snapshot = null;
    }

    public List<SceneRuntime>.Enumerator GetEnumerator() => _items.GetEnumerator();
    IEnumerator<SceneRuntime> IEnumerable<SceneRuntime>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
