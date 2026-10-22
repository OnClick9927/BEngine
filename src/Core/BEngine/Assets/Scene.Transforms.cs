namespace BEngine;

public sealed partial class Scene
{
    private readonly FrameDataBuffer<TransformBatchEntry> _transformBatch = new();
    private readonly List<Transform> _transformTraversal = [];
    private long _transformBatchRevision = -1;
    private readonly record struct TransformBatchEntry(Transform Transform);

    /// <summary>Synchronizes all authored world poses in a reusable parent-first batch on the runtime thread.
    /// Property reads remain lazy; call this when a system needs most of the scene's transforms.</summary>
    public void SynchronizeTransforms()
    {
        ThrowIfDisposed();
        if (_transformBatchRevision != HierarchyRevision)
        {
            _transformBatch.Clear(); _transformTraversal.Clear();
            _transformBatch.EnsureCapacity(_gameObjects.Count);
            for (var i = _gameObjects.Count - 1; i >= 0; i--)
                if (_gameObjects[i].TransformUnchecked.ParentUnchecked is null)
                    _transformTraversal.Add(_gameObjects[i].TransformUnchecked);
            while (_transformTraversal.Count > 0)
            {
                var last = _transformTraversal.Count - 1; var transform = _transformTraversal[last];
                _transformTraversal.RemoveAt(last); _transformBatch.Add(new(transform));
                var children = transform.ChildrenUnchecked;
                for (var i = children.Count - 1; i >= 0; i--) _transformTraversal.Add(children[i]);
            }
            _transformBatchRevision = HierarchyRevision;
        }
        foreach (ref readonly var entry in _transformBatch.WrittenSpan) entry.Transform.SynchronizeWorldPoseUnchecked();
    }
}
