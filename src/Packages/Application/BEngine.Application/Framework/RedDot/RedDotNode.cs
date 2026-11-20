using System;
using System.Collections.Generic;

namespace BEngine.App.RedDot;

/// <summary>Read-only node identity. Root uses the empty path; total includes local and descendant counts.</summary>
public sealed class RedDotNode
{
    internal RedDotNode(string path, RedDotNode? parent) { Path = path; Parent = parent; }
    internal readonly Dictionary<string, RedDotNode> ChildMap = new(StringComparer.Ordinal);
    internal RedDotSubscription[] Listeners = [];
    internal bool Queued;
    internal long PreviousCount;
    public string Path { get; }
    public RedDotNode? Parent { get; }
    public IReadOnlyCollection<RedDotNode> Children => ChildMap.Values;
    public long LocalCount { get; internal set; }
    public long Count { get; internal set; }
    public bool IsAttached { get; internal set; } = true;
}

internal sealed class RedDotSubscription(RedDotNode node, Action<RedDotChange> callback) : IDisposable
{
    private RedDotNode? _node = node;
    internal Action<RedDotChange>? Callback = callback;
    internal void Detach() { Callback = null; _node = null; }
    public void Dispose()
    {
        var current = _node;
        Detach();
        if (current is null) return;
        var entries = current.Listeners;
        var index = Array.IndexOf(entries, this);
        if (index < 0) return;
        if (entries.Length == 1) { current.Listeners = []; return; }
        var next = new RedDotSubscription[entries.Length - 1];
        Array.Copy(entries, 0, next, 0, index);
        Array.Copy(entries, index + 1, next, index, entries.Length - index - 1);
        current.Listeners = next;
    }
}
