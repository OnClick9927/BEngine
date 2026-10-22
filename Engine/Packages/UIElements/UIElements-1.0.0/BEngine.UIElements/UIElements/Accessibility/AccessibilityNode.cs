namespace BEngine.UIElements;

public sealed record AccessibilityNode(
    int Id,
    int? ParentId,
    AccessibilityRole Role,
    string Name,
    string Description,
    string Value,
    AccessibilityState State,
    UIElementRect Bounds,
    float? MinimumValue,
    float? MaximumValue,
    AccessibilityLiveRegion LiveRegion);

public sealed record AccessibilitySnapshot(
    long Revision,
    IReadOnlyList<AccessibilityNode> Nodes);

public sealed record AccessibilityEvent(
    UIDocument Document,
    AccessibilityEventKind Kind,
    int? NodeId = null,
    string Message = "",
    AccessibilityLiveRegion LiveRegion = AccessibilityLiveRegion.Off);

public interface IUIAccessibilityBridge
{
    bool IsScreenReaderActive { get; }
    void UpdateHierarchy(UIDocument document, AccessibilitySnapshot snapshot);
    void Notify(AccessibilityEvent accessibilityEvent);
}

public static class UIAccessibility
{
    private static IUIAccessibilityBridge? _bridge;

    public static IUIAccessibilityBridge? bridge
    {
        get => Volatile.Read(ref _bridge);
        set => Volatile.Write(ref _bridge, value);
    }

    public static bool screenReaderActive => (bridge ?? PlatformAccessibilityBridge.Instance).IsScreenReaderActive;
    public static event Action<AccessibilityEvent>? eventReceived;

    // Most games do not have a screen reader attached.  Keeping the live
    // accessibility event path cold in that case avoids allocating an
    // AccessibilityEvent and a platform event for every animated label/style
    // change.  A subscriber (tests, editor integrations, or an accessibility
    // bridge) opts back in automatically.
    internal static bool liveUpdatesEnabled => eventReceived is not null || screenReaderActive;

    internal static bool ShouldPublish(UIDocument document, AccessibilityEventKind kind, int? nodeId) =>
        eventReceived is not null || bridge is not null ||
        PlatformAccessibilityBridge.Instance.ShouldPublish(document, kind, nodeId);

    internal static void Publish(AccessibilityEvent value)
    {
        if (!liveUpdatesEnabled) return;
        eventReceived?.Invoke(value);
        (bridge ?? PlatformAccessibilityBridge.Instance).Notify(value);
    }

    internal static void PublishHierarchy(UIDocument document, AccessibilitySnapshot snapshot) =>
        (bridge ?? PlatformAccessibilityBridge.Instance).UpdateHierarchy(document, snapshot);

    internal static void RemoveHierarchy(UIDocument document) => PlatformAccessibilityBridge.Instance.Remove(document);
    internal static void PumpActions(UIDocument document) => PlatformAccessibilityBridge.Instance.Pump(document);
}
