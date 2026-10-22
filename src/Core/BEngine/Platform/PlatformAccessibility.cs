namespace BEngine.Platform;

public enum PlatformAccessibilityRole
{
    Group, Text, Heading, Button, Toggle, RadioButton, TextField, SearchField, Slider, ComboBox,
    Image, ProgressBar, List, ListItem, ScrollView, Menu, MenuItem, Link
}

[Flags]
public enum PlatformAccessibilityState
{
    None = 0, Disabled = 1 << 0, Focused = 1 << 1, Checked = 1 << 2, Selected = 1 << 3,
    ReadOnly = 1 << 4, Password = 1 << 5
}

public enum PlatformAccessibilityAction { Focus, Activate, Increment, Decrement }
public enum PlatformAccessibilityEventKind { HierarchyChanged, NodeChanged, FocusChanged, Announcement }
public enum PlatformAccessibilityLiveRegion { Off, Polite, Assertive }

public readonly record struct PlatformAccessibilityBounds(float X, float Y, float Width, float Height);

public sealed record PlatformAccessibilityNode(
    int Id, int? ParentId, PlatformAccessibilityRole Role, string Name, string Description, string Value,
    PlatformAccessibilityState State, PlatformAccessibilityBounds Bounds, float? MinimumValue,
    float? MaximumValue, PlatformAccessibilityLiveRegion LiveRegion);

public sealed record PlatformAccessibilitySnapshot(long Revision, IReadOnlyList<PlatformAccessibilityNode> Nodes);

public sealed record PlatformAccessibilityEvent(long DocumentId, PlatformAccessibilityEventKind Kind,
    int? NodeId = null, string Message = "", PlatformAccessibilityLiveRegion LiveRegion = PlatformAccessibilityLiveRegion.Off);

public interface IPlatformAccessibilityAdapter : IDisposable
{
    bool IsScreenReaderActive { get; }
    void AttachWindow(nint nativeHandle);
    void UpdateHierarchy(long documentId, PlatformAccessibilitySnapshot snapshot,
        Func<int, PlatformAccessibilityAction, bool> performAction);
    void RemoveHierarchy(long documentId);
    void Notify(PlatformAccessibilityEvent accessibilityEvent);
}

// Optional early rejection of events which Notify would already discard.
// Existing adapters keep receiving every event without implementing this.
public interface IPlatformAccessibilityEventFilter
{
    bool ShouldNotify(PlatformAccessibilityEventKind kind, long documentId, int? nodeId);
}

public static class PlatformAccessibility
{
    private static readonly object Gate = new();
    private static IPlatformAccessibilityAdapter? _adapter;
    private static nint _windowHandle;

    public static IPlatformAccessibilityAdapter? adapter
    {
        get => Volatile.Read(ref _adapter);
        set
        {
            IPlatformAccessibilityAdapter? previous;
            nint handle;
            lock (Gate) { previous = _adapter; Volatile.Write(ref _adapter, value); handle = _windowHandle; }
            if (!ReferenceEquals(previous, value)) previous?.Dispose();
            value?.AttachWindow(handle);
        }
    }

    public static nint nativeWindowHandle { get { lock (Gate) return _windowHandle; } }

    public static void AttachWindow(nint nativeHandle)
    {
        IPlatformAccessibilityAdapter? current;
        lock (Gate) { _windowHandle = nativeHandle; current = _adapter; }
        current?.AttachWindow(nativeHandle);
    }
}
