using System.Collections.Concurrent;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace BEngine.Platform;

public sealed class WindowsAccessibilityAdapter : IPlatformAccessibilityAdapter, IPlatformAccessibilityEventFilter
{
    private const int GwlWndProc = -4;
    private const uint WmGetObject = 0x003D;
    private const int UiaRootObjectId = -25;
    private readonly object _gate = new();
    private readonly Dictionary<long, DocumentState> _documents = [];
    private readonly WindowsRootProvider _root;
    private readonly NativeMethods.WndProc _windowProcedure;
    private nint _window;
    private nint _previousProcedure;
    private long _nextListenerProbe;
    private int _listenersActive;
    private bool _disposed;

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IRawElementProviderSimple))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IRawElementProviderFragment))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IRawElementProviderFragmentRoot))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IInvokeProvider))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IToggleProvider))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IRangeValueProvider))]
    // UIA calls these implementations through COM, outside the managed call graph.
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(WindowsRootProvider))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(WindowsNodeProvider))]
    public WindowsAccessibilityAdapter()
    {
        _root = new WindowsRootProvider(this);
        _windowProcedure = WindowProcedure;
    }

    public bool IsScreenReaderActive => OperatingSystem.IsWindows() && HasListeners();
    public int NodeCount { get { lock (_gate) return _documents.Values.Sum(value => value.Nodes.Count); } }

    public void AttachWindow(nint nativeHandle)
    {
        if (!OperatingSystem.IsWindows() || _disposed || nativeHandle == _window) return;
        RestoreWindowProcedure();
        _window = nativeHandle;
        Volatile.Write(ref _nextListenerProbe, 0);
        if (_window == 0) return;
        var pointer = Marshal.GetFunctionPointerForDelegate(_windowProcedure);
        _previousProcedure = NativeMethods.SetWindowLongPtr(_window, GwlWndProc, pointer);
    }

    public void UpdateHierarchy(long documentId, PlatformAccessibilitySnapshot snapshot,
        Func<int, PlatformAccessibilityAction, bool> performAction)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(performAction);
        lock (_gate) _documents[documentId] = new(snapshot,
            snapshot.Nodes.ToDictionary(node => node.Id), performAction);
        RaiseEvent(20002, documentId, null);
    }

    public void RemoveHierarchy(long documentId)
    {
        lock (_gate) _documents.Remove(documentId);
        RaiseEvent(20002, documentId, null);
    }

    public bool ShouldNotify(PlatformAccessibilityEventKind kind, long documentId, int? nodeId)
    {
        if (kind == PlatformAccessibilityEventKind.NodeChanged)
        {
            // A node mutation is useful only after its hierarchy has been
            // published. Falling back to the desktop root for an unknown node
            // turns every animated style property into a global UIA event.
            lock (_gate)
                return _documents.TryGetValue(documentId, out var document) &&
                    nodeId is { } id && document.Nodes.ContainsKey(id);
        }
        return true;
    }

    public void Notify(PlatformAccessibilityEvent accessibilityEvent)
    {
        // Recheck at delivery: the hierarchy may have been removed after the
        // caller's allocation-free eligibility probe.
        if (!ShouldNotify(accessibilityEvent.Kind, accessibilityEvent.DocumentId, accessibilityEvent.NodeId)) return;
        var eventId = accessibilityEvent.Kind switch
        {
            PlatformAccessibilityEventKind.FocusChanged => 20005,
            PlatformAccessibilityEventKind.HierarchyChanged => 20002,
            PlatformAccessibilityEventKind.Announcement => 20024,
            _ => 20004
        };
        RaiseEvent(eventId, accessibilityEvent.DocumentId, accessibilityEvent.NodeId);
    }

    internal IReadOnlyList<(long DocumentId, PlatformAccessibilityNode Node)> Roots()
    {
        lock (_gate) return _documents.SelectMany(document => document.Value.Snapshot.Nodes
            .Where(node => node.ParentId is null).Select(node => (document.Key, node))).ToArray();
    }

    internal IReadOnlyList<PlatformAccessibilityNode> Children(long documentId, int? parentId)
    {
        lock (_gate) return _documents.TryGetValue(documentId, out var document)
            ? document.Snapshot.Nodes.Where(node => node.ParentId == parentId).ToArray() : [];
    }

    internal PlatformAccessibilityNode? Node(long documentId, int nodeId)
    {
        lock (_gate) return _documents.TryGetValue(documentId, out var document) &&
            document.Nodes.TryGetValue(nodeId, out var node) ? node : null;
    }

    internal bool Perform(long documentId, int nodeId, PlatformAccessibilityAction action)
    {
        Func<int, PlatformAccessibilityAction, bool>? callback;
        lock (_gate) callback = _documents.GetValueOrDefault(documentId)?.PerformAction;
        return callback?.Invoke(nodeId, action) == true;
    }

    internal nint Window => _window;

    private nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == WmGetObject && unchecked((int)(long)lParam) == UiaRootObjectId)
        {
            // An attaching client need not wait for the next periodic probe.
            Volatile.Write(ref _listenersActive, 1);
            Volatile.Write(ref _nextListenerProbe, Environment.TickCount64 + 250);
            return NativeMethods.UiaReturnRawElementProvider(hwnd, wParam, lParam, _root);
        }
        return _previousProcedure != 0
            ? NativeMethods.CallWindowProc(_previousProcedure, hwnd, message, wParam, lParam)
            : NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void RaiseEvent(int eventId, long documentId, int? nodeId)
    {
        if (!OperatingSystem.IsWindows() || _window == 0 || !HasListeners()) return;
        IRawElementProviderSimple provider = nodeId is { } id && Node(documentId, id) is not null
            ? new WindowsNodeProvider(this, documentId, id) : _root;
        _ = NativeMethods.UiaRaiseAutomationEvent(provider, eventId);
    }

    private bool HasListeners()
    {
        // This property is queried for every changed UI property, including
        // paint-only animations. The native listener query is comparatively
        // expensive and its answer changes far less often than a game frame.
        var now = Environment.TickCount64;
        if (now >= Volatile.Read(ref _nextListenerProbe))
        {
            Volatile.Write(ref _listenersActive, NativeMethods.UiaClientsAreListening() ? 1 : 0);
            Volatile.Write(ref _nextListenerProbe, now + 250);
        }
        return Volatile.Read(ref _listenersActive) != 0;
    }

    private void RestoreWindowProcedure()
    {
        if (_window != 0 && _previousProcedure != 0 && OperatingSystem.IsWindows())
            NativeMethods.SetWindowLongPtr(_window, GwlWndProc, _previousProcedure);
        _previousProcedure = 0; _window = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        RestoreWindowProcedure();
        lock (_gate) _documents.Clear();
        _disposed = true;
    }

    private sealed record DocumentState(PlatformAccessibilitySnapshot Snapshot,
        Dictionary<int, PlatformAccessibilityNode> Nodes, Func<int, PlatformAccessibilityAction, bool> PerformAction);

    private static class NativeMethods
    {
        internal delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint procedure);
        [DllImport("user32.dll")] internal static extern nint CallWindowProc(nint procedure, nint hwnd, uint message, nint wParam, nint lParam);
        [DllImport("user32.dll")] internal static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ClientToScreen(nint hwnd, ref NativePoint point);
        [DllImport("uiautomationcore.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UiaClientsAreListening();
        [DllImport("uiautomationcore.dll")] internal static extern nint UiaReturnRawElementProvider(nint hwnd, nint wParam, nint lParam, [MarshalAs(UnmanagedType.Interface)] IRawElementProviderSimple provider);
        [DllImport("uiautomationcore.dll")] internal static extern int UiaHostProviderFromHwnd(nint hwnd, [MarshalAs(UnmanagedType.Interface)] out IRawElementProviderSimple? provider);
        [DllImport("uiautomationcore.dll")] internal static extern int UiaRaiseAutomationEvent([MarshalAs(UnmanagedType.Interface)] IRawElementProviderSimple provider, int eventId);
    }

    [StructLayout(LayoutKind.Sequential)] internal struct NativePoint { internal int X; internal int Y; }

    internal UiaRect ToScreen(PlatformAccessibilityBounds bounds)
    {
        var point = new NativePoint { X = (int)Math.Round(bounds.X), Y = (int)Math.Round(bounds.Y) };
        if (_window != 0) NativeMethods.ClientToScreen(_window, ref point);
        return new(point.X, point.Y, bounds.Width, bounds.Height);
    }

    internal IRawElementProviderSimple? HostProvider()
    {
        if (_window == 0) return null;
        _ = NativeMethods.UiaHostProviderFromHwnd(_window, out var provider);
        return provider;
    }
}
