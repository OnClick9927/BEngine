using System;
using System.Collections.Generic;

namespace BEngine.App.RedDot;

/// <summary>Incremental path tree; no scene/global state and no worker threads.
/// Use and dispose this service and its subscriptions on the domain thread.</summary>
public sealed class RedDotService : GameService, IRedDotService
{
    private readonly Dictionary<string, RedDotNode> _nodes = new(StringComparer.Ordinal);
    private readonly Queue<RedDotNode> _pending = new();
    private bool _flushing;
    private bool _disposed;

    public RedDotService()
    {
        Root = new RedDotNode(string.Empty, null);
        _nodes.Add(string.Empty, Root);
    }
    public RedDotNode Root { get; }

    public RedDotNode EnsurePath(string path)
    {
        CheckAlive();
        ArgumentNullException.ThrowIfNull(path);
        // Only validated paths enter the index. Repeated updates should not rescan every segment.
        if (_nodes.TryGetValue(path, out var existing)) return existing;
        ValidatePath(path);
        var parent = Root;
        var start = 0;
        while (start < path.Length)
        {
            var separator = path.IndexOf('/', start);
            var end = separator < 0 ? path.Length : separator;
            var fullPath = end == path.Length ? path : path[..end];
            if (!_nodes.TryGetValue(fullPath, out var node))
            {
                node = new RedDotNode(fullPath, parent);
                _nodes.Add(fullPath, node);
                parent.ChildMap.Add(path[start..end], node);
            }
            parent = node;
            start = end + 1;
        }
        return parent;
    }

    public bool TryGetNode(string path, out RedDotNode? node)
    {
        CheckAlive();
        ArgumentNullException.ThrowIfNull(path);
        if (_nodes.TryGetValue(path, out node)) return true;
        ValidatePath(path);
        return false;
    }
    public long GetCount(string path) => TryGetNode(path, out var node) ? node!.Count : 0;

    public void SetCount(string path, long count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        SetCountCore(EnsurePath(path), count);
    }
    private void SetCountCore(RedDotNode node, long count)
    {
        var difference = count - node.LocalCount;
        if (difference == 0) return;
        // Validate the whole ancestor chain before changing any existing count.
        for (var current = node; current is not null; current = current.Parent)
            _ = checked(current.Count + difference);
        node.LocalCount = count;
        for (var current = node; current is not null; current = current.Parent)
        {
            Enqueue(current);
            current.Count += difference;
        }
    }
    public void AddCount(string path, long amount)
    {
        var node = EnsurePath(path);
        var count = checked(node.LocalCount + amount);
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        SetCountCore(node, count);
    }

    public IDisposable Subscribe(string path, Action<RedDotChange> callback, bool notifyImmediately = true)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var node = EnsurePath(path);
        var subscription = new RedDotSubscription(node, callback);
        var next = new RedDotSubscription[node.Listeners.Length + 1];
        Array.Copy(node.Listeners, next, node.Listeners.Length);
        next[^1] = subscription;
        node.Listeners = next;
        if (notifyImmediately)
        {
            try { callback(new RedDotChange(path, node.Count, node.Count, node.LocalCount)); }
            catch { subscription.Dispose(); throw; }
        }
        return subscription;
    }

    /// <summary>Removes a subtree and its subscriptions. Ancestor listeners receive the new total on Flush.
    /// Removed node identities remain detached; registering the same path creates a new identity.</summary>
    public bool RemoveBranch(string path)
    {
        CheckAlive();
        ValidatePath(path);
        if (path.Length == 0) { Clear(); return true; }
        if (!_nodes.TryGetValue(path, out var node)) return false;
        var count = node.Count;
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            Enqueue(parent);
            parent.Count -= count;
        }
        var separator = path.LastIndexOf('/');
        node.Parent!.ChildMap.Remove(path[(separator + 1)..]);
        Detach(node);
        return true;
    }

    /// <summary>Clears all counts, descendant nodes and descendant subscriptions; root listeners remain.</summary>
    public void Clear()
    {
        CheckAlive();
        foreach (var child in Root.ChildMap.Values) Detach(child);
        Root.ChildMap.Clear();
        if (Root.Count != 0 || Root.LocalCount != 0)
        {
            Enqueue(Root);
            Root.Count = Root.LocalCount = 0;
        }
    }

    public void Flush()
    {
        CheckAlive();
        if (_flushing) return;
        _flushing = true;
        List<Exception>? failures = null;
        try
        {
            while (_pending.Count > 0 && !_disposed)
            {
                var node = _pending.Dequeue();
                node.Queued = false;
                if (!node.IsAttached || node.PreviousCount == node.Count) continue;
                var change = new RedDotChange(node.Path, node.PreviousCount, node.Count, node.LocalCount);
                var listeners = node.Listeners;
                foreach (var entry in listeners)
                {
                    if (_disposed || !node.IsAttached) break;
                    if (entry.Callback is not { } callback) continue;
                    try { callback(change); }
                    catch (Exception error) { (failures ??= []).Add(error); }
                }
            }
        }
        finally { _flushing = false; }
        if (failures is not null) throw new AggregateException("Red-dot listeners failed after counts were committed.", failures);
    }

    protected override void OnLateUpdate(Fix64 deltaTime) => Flush();
    protected override void OnDispose()
    {
        _disposed = true;
        Detach(Root);
        _pending.Clear();
        _nodes.Clear();
    }

    private void Enqueue(RedDotNode node)
    {
        if (node.Queued) return;
        node.Queued = true;
        node.PreviousCount = node.Count;
        _pending.Enqueue(node);
    }
    private void Detach(RedDotNode node)
    {
        // Iterative traversal avoids stack overflow for deeply nested paths.
        var remaining = new Queue<RedDotNode>();
        remaining.Enqueue(node);
        while (remaining.Count > 0)
        {
            var current = remaining.Dequeue();
            foreach (var child in current.ChildMap.Values) remaining.Enqueue(child);
            foreach (var listener in current.Listeners) listener.Detach();
            current.Listeners = [];
            current.ChildMap.Clear();
            current.Count = current.LocalCount = 0;
            current.IsAttached = false;
            _nodes.Remove(current.Path);
        }
    }
    private void CheckAlive() => ObjectDisposedException.ThrowIf(_disposed, this);
    private static void ValidatePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length == 0) return;
        if (path[0] == '/' || path[^1] == '/' || path.Contains("//", StringComparison.Ordinal))
            throw new ArgumentException("Use an empty root or slash-separated, non-empty path segments.", nameof(path));
        foreach (var segment in path.AsSpan().Split('/'))
        {
            var value = path.AsSpan()[segment];
            if (value.SequenceEqual(".".AsSpan()) || value.SequenceEqual("..".AsSpan()))
                throw new ArgumentException("Relative path segments are not red-dot keys.", nameof(path));
        }
    }
}
