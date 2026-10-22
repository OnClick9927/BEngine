using BEngine.Platform;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

internal sealed class PlatformAccessibilityBridge : IUIAccessibilityBridge
{
    internal static PlatformAccessibilityBridge Instance { get; } = new();
    private readonly ConditionalWeakTable<UIDocument, DocumentIdentity> _identities = new();
    private readonly ConditionalWeakTable<UIDocument, ActionQueue> _actions = new();
    private readonly ConditionalWeakTable<UIDocument, DocumentIdentity>.CreateValueCallback _createIdentity;
    private long _nextDocumentId;

    internal PlatformAccessibilityBridge() => _createIdentity = CreateIdentity;

    public bool IsScreenReaderActive => PlatformAccessibility.adapter?.IsScreenReaderActive == true;

    public void UpdateHierarchy(UIDocument document, AccessibilitySnapshot snapshot)
    {
        if (PlatformAccessibility.adapter is not { } adapter) return;
        var id = Identity(document);
        adapter.UpdateHierarchy(id, new PlatformAccessibilitySnapshot(snapshot.Revision,
            snapshot.Nodes.Select(Map).ToArray()),
            (nodeId, action) =>
            {
                _actions.GetOrCreateValue(document).Items.Enqueue((nodeId, action));
                return true;
            });
    }

    public void Notify(AccessibilityEvent accessibilityEvent)
    {
        PlatformAccessibility.adapter?.Notify(new PlatformAccessibilityEvent(
            Identity(accessibilityEvent.Document),
            (PlatformAccessibilityEventKind)(int)accessibilityEvent.Kind,
            accessibilityEvent.NodeId,
            accessibilityEvent.Message,
            (PlatformAccessibilityLiveRegion)(int)accessibilityEvent.LiveRegion));
    }

    internal bool ShouldPublish(UIDocument document, AccessibilityEventKind kind, int? nodeId) =>
        PlatformAccessibility.adapter is not IPlatformAccessibilityEventFilter filter ||
        filter.ShouldNotify((PlatformAccessibilityEventKind)(int)kind, Identity(document), nodeId);

    internal void Remove(UIDocument document)
    {
        if (_identities.TryGetValue(document, out var identity))
            PlatformAccessibility.adapter?.RemoveHierarchy(identity.Id);
    }

    internal void Pump(UIDocument document)
    {
        if (!_actions.TryGetValue(document, out var queue)) return;
        while (queue.Items.TryDequeue(out var request))
            document.PerformAccessibilityAction(request.NodeId, (AccessibilityAction)(int)request.Action);
    }

    private long Identity(UIDocument document) =>
        _identities.GetValue(document, _createIdentity).Id;

    private DocumentIdentity CreateIdentity(UIDocument document) =>
        new(Interlocked.Increment(ref _nextDocumentId));

    private static PlatformAccessibilityNode Map(AccessibilityNode node) => new(
        node.Id, node.ParentId, (PlatformAccessibilityRole)((int)node.Role - 1),
        node.Name, node.Description, node.Value, (PlatformAccessibilityState)(int)node.State,
        new((float)node.Bounds.X, (float)node.Bounds.Y, (float)node.Bounds.Width, (float)node.Bounds.Height),
        node.MinimumValue, node.MaximumValue, (PlatformAccessibilityLiveRegion)(int)node.LiveRegion);

    private sealed record DocumentIdentity(long Id);
    private sealed class ActionQueue
    {
        internal System.Collections.Concurrent.ConcurrentQueue<(int NodeId, PlatformAccessibilityAction Action)> Items { get; } = new();
    }
}
