using System;
using System.Collections.Generic;
using System.Linq;
using Android.Content;
using Android.Graphics;
using Android.Opengl;
using Android.OS;
using Android.Views;
using Android.Views.Accessibility;
using BEngine.Platform;
using Action = Android.Views.Accessibility.Action;

#pragma warning disable CA1422 // Compatibility APIs remain required for the Android 26 minimum target.

namespace BEngine.Player.Hosts.Android;

internal sealed class AccessibleGameSurface(Context context, AndroidAccessibilityAdapter accessibility) :
    GLSurfaceView(context)
{
    public override AccessibilityNodeProvider AccessibilityNodeProvider => accessibility.Provider;
    public override bool OnHoverEvent(MotionEvent? e) => e is not null && accessibility.DispatchHover(e) || base.OnHoverEvent(e);
}

internal sealed class AndroidAccessibilityAdapter : IPlatformAccessibilityAdapter
{
    private readonly object _gate = new();
    private readonly Dictionary<long, DocumentState> _documents = [];
    private readonly Dictionary<(long Document, int Node), int> _virtualIds = [];
    private readonly Dictionary<int, (long Document, int Node)> _nodesByVirtualId = [];
    private int _nextVirtualId = 1;
    private int _accessibilityFocusId = AccessibilityNodeProvider.HostViewId;
    private int _hoverId = AccessibilityNodeProvider.HostViewId;
    private AccessibleGameSurface? _surface;

    internal AndroidAccessibilityAdapter() => Provider = new VirtualNodeProvider(this);
    internal AccessibilityNodeProvider Provider { get; }

    public bool IsScreenReaderActive
    {
        get
        {
            var manager = _surface?.Context?.GetSystemService(Context.AccessibilityService) as AccessibilityManager;
            return manager?.IsEnabled == true && manager.IsTouchExplorationEnabled;
        }
    }

    internal void AttachSurface(AccessibleGameSurface surface)
    {
        _surface = surface;
        surface.ImportantForAccessibility = ImportantForAccessibility.Yes;
        surface.ContentDescription = "BEngine Player";
    }

    public void AttachWindow(nint nativeHandle) { }

    public void UpdateHierarchy(long documentId, PlatformAccessibilitySnapshot snapshot,
        Func<int, PlatformAccessibilityAction, bool> performAction)
    {
        lock (_gate)
        {
            _documents[documentId] = new(snapshot, snapshot.Nodes.ToDictionary(node => node.Id), performAction);
            foreach (var pair in _virtualIds.Where(pair => pair.Key.Document == documentId && !snapshot.Nodes.Any(node => node.Id == pair.Key.Node)).ToArray())
            {
                _virtualIds.Remove(pair.Key); _nodesByVirtualId.Remove(pair.Value);
                if (_accessibilityFocusId == pair.Value) _accessibilityFocusId = AccessibilityNodeProvider.HostViewId;
                if (_hoverId == pair.Value) _hoverId = AccessibilityNodeProvider.HostViewId;
            }
            foreach (var node in snapshot.Nodes) VirtualId(documentId, node.Id);
        }
        PostEvent(EventTypes.WindowContentChanged);
    }

    public void RemoveHierarchy(long documentId)
    {
        lock (_gate)
        {
            _documents.Remove(documentId);
            foreach (var pair in _virtualIds.Where(pair => pair.Key.Document == documentId).ToArray())
            {
                _virtualIds.Remove(pair.Key);
                _nodesByVirtualId.Remove(pair.Value);
                if (_accessibilityFocusId == pair.Value) _accessibilityFocusId = AccessibilityNodeProvider.HostViewId;
                if (_hoverId == pair.Value) _hoverId = AccessibilityNodeProvider.HostViewId;
            }
        }
        PostEvent(EventTypes.WindowContentChanged);
    }

    public void Notify(PlatformAccessibilityEvent accessibilityEvent)
    {
        if (accessibilityEvent.Kind == PlatformAccessibilityEventKind.Announcement &&
            !string.IsNullOrWhiteSpace(accessibilityEvent.Message))
        {
            var surface = _surface;
            if (surface is null || !CanSendEvents(surface)) return;
            surface.Post(() =>
            {
                if (!ReferenceEquals(_surface, surface) || !CanSendEvents(surface)) return;
                using var announcement = AccessibilityEvent.Obtain(EventTypes.Announcement) ??
                    throw new InvalidOperationException("Android could not allocate an accessibility event.");
                announcement.ContentDescription = accessibilityEvent.Message;
                try { surface.SendAccessibilityEventUnchecked(announcement); }
                catch (Java.Lang.IllegalStateException) when (!CanSendEvents(surface)) { }
            });
            return;
        }
        var id = AccessibilityNodeProvider.HostViewId;
        lock (_gate)
        {
            if (accessibilityEvent.NodeId is { } eventNode && _virtualIds.TryGetValue((accessibilityEvent.DocumentId, eventNode), out var nodeId)) id = nodeId;
        }
        PostEvent(accessibilityEvent.Kind == PlatformAccessibilityEventKind.FocusChanged
            ? EventTypes.ViewFocused : EventTypes.WindowContentChanged, id);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _documents.Clear();
            _virtualIds.Clear();
            _nodesByVirtualId.Clear();
        }
        _surface = null;
        Provider.Dispose();
    }

    private void PostEvent(EventTypes eventType, int virtualId = AccessibilityNodeProvider.HostViewId)
    {
        var surface = _surface;
        if (surface is null || !CanSendEvents(surface)) return;
        surface.Post(() =>
        {
            if (!ReferenceEquals(_surface, surface) || !CanSendEvents(surface)) return;
            using var message = AccessibilityEvent.Obtain(eventType);
            if (message is null) return;
            message.PackageName = surface.Context!.PackageName;
            message.SetSource(surface, virtualId);
            try { surface.Parent?.RequestSendAccessibilityEvent(surface, message); }
            catch (Java.Lang.IllegalStateException) when (!CanSendEvents(surface)) { }
        });
    }

    private static bool CanSendEvents(View surface) =>
        (surface.Context?.GetSystemService(Context.AccessibilityService) as AccessibilityManager)?.IsEnabled == true;

    internal bool DispatchHover(MotionEvent e)
    {
        if (!IsScreenReaderActive) return false;
        var next = AccessibilityNodeProvider.HostViewId;
        lock (_gate)
        {
            if (e.Action is MotionEventActions.HoverEnter or MotionEventActions.HoverMove)
            {
                var x = e.GetX(); var y = e.GetY();
                var hits = _documents.SelectMany(document => document.Value.Snapshot.Nodes
                    .Where(node => x >= node.Bounds.X && y >= node.Bounds.Y && x < node.Bounds.X + node.Bounds.Width && y < node.Bounds.Y + node.Bounds.Height)
                    .Select(node => (document.Key, Node: node)))
                    .OrderBy(hit => hit.Node.Bounds.Width * hit.Node.Bounds.Height).ToArray();
                if (hits.Length > 0) next = VirtualId(hits[0].Key, hits[0].Node.Id);
            }
            if (next == _hoverId) return next != AccessibilityNodeProvider.HostViewId;
            if (_hoverId != AccessibilityNodeProvider.HostViewId) PostEvent(EventTypes.ViewHoverExit, _hoverId);
            _hoverId = next;
            if (next != AccessibilityNodeProvider.HostViewId) PostEvent(EventTypes.ViewHoverEnter, next);
        }
        return next != AccessibilityNodeProvider.HostViewId;
    }

    private int VirtualId(long documentId, int nodeId)
    {
        if (_virtualIds.TryGetValue((documentId, nodeId), out var value)) return value;
        value = _nextVirtualId++;
        _virtualIds[(documentId, nodeId)] = value;
        _nodesByVirtualId[value] = (documentId, nodeId);
        return value;
    }

    private AccessibilityNodeInfo? CreateNode(int virtualViewId)
    {
        var surface = _surface;
        if (surface is null) return null;
        lock (_gate)
        {
            if (virtualViewId == AccessibilityNodeProvider.HostViewId)
            {
                var host = AccessibilityNodeInfo.Obtain(surface);
                if (host is null) return null;
                host.PackageName = surface.Context!.PackageName;
                host.ClassName = "android.opengl.GLSurfaceView";
                host.ContentDescription = "BEngine Player";
                foreach (var entry in _documents)
                    foreach (var root in entry.Value.Snapshot.Nodes.Where(node => node.ParentId is null))
                        host.AddChild(surface, VirtualId(entry.Key, root.Id));
                return host;
            }

            if (!_nodesByVirtualId.TryGetValue(virtualViewId, out var identity) ||
                !_documents.TryGetValue(identity.Document, out var document) ||
                !document.Nodes.TryGetValue(identity.Node, out var node)) return null;

            var info = AccessibilityNodeInfo.Obtain(surface, virtualViewId);
            if (info is null) return null;
            info.PackageName = surface.Context!.PackageName;
            info.ClassName = AndroidClass(node.Role);
            info.ContentDescription = string.IsNullOrWhiteSpace(node.Description)
                ? node.Name : $"{node.Name}. {node.Description}";
            info.Text = node.Role is PlatformAccessibilityRole.Text or PlatformAccessibilityRole.Heading
                ? node.Name : node.Value;
            info.Enabled = !node.State.HasFlag(PlatformAccessibilityState.Disabled);
            info.Focusable = IsInteractive(node.Role);
            info.Focused = node.State.HasFlag(PlatformAccessibilityState.Focused);
            info.AccessibilityFocused = virtualViewId == _accessibilityFocusId;
            info.Clickable = node.Role is PlatformAccessibilityRole.Button or PlatformAccessibilityRole.Toggle or
                PlatformAccessibilityRole.RadioButton or PlatformAccessibilityRole.Link or PlatformAccessibilityRole.MenuItem;
            info.Checkable = node.Role is PlatformAccessibilityRole.Toggle or PlatformAccessibilityRole.RadioButton;
            info.Checked = node.State.HasFlag(PlatformAccessibilityState.Checked);
            info.Selected = node.State.HasFlag(PlatformAccessibilityState.Selected);
            info.Password = node.State.HasFlag(PlatformAccessibilityState.Password);
            info.Editable = node.Role is PlatformAccessibilityRole.TextField or PlatformAccessibilityRole.SearchField;
            info.Scrollable = node.Role is PlatformAccessibilityRole.Slider or PlatformAccessibilityRole.ScrollView;
            info.VisibleToUser = node.Bounds.Width > 0 && node.Bounds.Height > 0;
            info.SetSource(surface, virtualViewId);
            if (node.ParentId is { } parentId) info.SetParent(surface, VirtualId(identity.Document, parentId));
            else info.SetParent(surface);
            foreach (var child in document.Snapshot.Nodes.Where(value => value.ParentId == node.Id))
                info.AddChild(surface, VirtualId(identity.Document, child.Id));
            var left = (int)MathF.Round(node.Bounds.X);
            var top = (int)MathF.Round(node.Bounds.Y);
            var bounds = new global::Android.Graphics.Rect(left, top, left + Math.Max(1, (int)MathF.Round(node.Bounds.Width)),
                top + Math.Max(1, (int)MathF.Round(node.Bounds.Height)));
            var parentBounds = new global::Android.Graphics.Rect(bounds);
            if (node.ParentId is { } boundsParent && document.Nodes.TryGetValue(boundsParent, out var parentNode))
                parentBounds.Offset(-(int)MathF.Round(parentNode.Bounds.X), -(int)MathF.Round(parentNode.Bounds.Y));
            info.SetBoundsInParent(parentBounds);
            var location = new int[2];
            surface.GetLocationOnScreen(location);
            bounds.Offset(location[0], location[1]);
            info.SetBoundsInScreen(bounds);
            info.AddAction(info.AccessibilityFocused ? AccessibilityNodeInfo.AccessibilityAction.ActionClearAccessibilityFocus :
                AccessibilityNodeInfo.AccessibilityAction.ActionAccessibilityFocus);
            if (info.Clickable) info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionClick);
            if (node.Role == PlatformAccessibilityRole.Slider)
            {
                info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionScrollForward);
                info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionScrollBackward);
                if (node.MinimumValue is { } minimum && node.MaximumValue is { } maximum &&
                    float.TryParse(node.Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var current))
                    info.SetRangeInfo(AccessibilityNodeInfo.RangeInfo.Obtain(RangeType.Float, minimum, maximum, current));
            }
            return info;
        }
    }

    private bool Perform(int virtualViewId, Action action)
    {
        if (virtualViewId == AccessibilityNodeProvider.HostViewId) return false;
        Func<int, PlatformAccessibilityAction, bool>? callback;
        (long Document, int Node) identity;
        lock (_gate)
        {
            if (!_nodesByVirtualId.TryGetValue(virtualViewId, out identity) ||
                !_documents.TryGetValue(identity.Document, out var document) || !document.Nodes.ContainsKey(identity.Node)) return false;
            callback = document.PerformAction;
            if (action == Action.AccessibilityFocus)
            {
                if (_accessibilityFocusId != virtualViewId && _accessibilityFocusId != AccessibilityNodeProvider.HostViewId)
                    PostEvent(EventTypes.ViewAccessibilityFocusCleared, _accessibilityFocusId);
                _accessibilityFocusId = virtualViewId;
                PostEvent(EventTypes.ViewAccessibilityFocused, virtualViewId);
                callback(identity.Node, PlatformAccessibilityAction.Focus);
                return true;
            }
            if (action == Action.ClearAccessibilityFocus)
            {
                if (_accessibilityFocusId != virtualViewId) return false;
                _accessibilityFocusId = AccessibilityNodeProvider.HostViewId;
                PostEvent(EventTypes.ViewAccessibilityFocusCleared, virtualViewId);
                return true;
            }
        }
        var mapped = action switch
        {
            Action.Focus or Action.AccessibilityFocus => PlatformAccessibilityAction.Focus,
            Action.Click => PlatformAccessibilityAction.Activate,
            Action.ScrollForward => PlatformAccessibilityAction.Increment,
            Action.ScrollBackward => PlatformAccessibilityAction.Decrement,
            _ => (PlatformAccessibilityAction)(-1)
        };
        if ((int)mapped < 0) return action == Action.ClearAccessibilityFocus;
        var result = callback(identity.Node, mapped);
        if (result) PostEvent(action == Action.Click ? EventTypes.ViewClicked : EventTypes.ViewFocused, virtualViewId);
        return result;
    }

    private static bool IsInteractive(PlatformAccessibilityRole role) => role is PlatformAccessibilityRole.Button or
        PlatformAccessibilityRole.Toggle or PlatformAccessibilityRole.RadioButton or PlatformAccessibilityRole.TextField or
        PlatformAccessibilityRole.SearchField or PlatformAccessibilityRole.Slider or PlatformAccessibilityRole.ComboBox or
        PlatformAccessibilityRole.MenuItem or PlatformAccessibilityRole.Link;

    private static string AndroidClass(PlatformAccessibilityRole role) => role switch
    {
        PlatformAccessibilityRole.Button => "android.widget.Button",
        PlatformAccessibilityRole.Toggle => "android.widget.CheckBox",
        PlatformAccessibilityRole.RadioButton => "android.widget.RadioButton",
        PlatformAccessibilityRole.TextField or PlatformAccessibilityRole.SearchField => "android.widget.EditText",
        PlatformAccessibilityRole.Slider => "android.widget.SeekBar",
        PlatformAccessibilityRole.Image => "android.widget.ImageView",
        PlatformAccessibilityRole.List => "android.widget.ListView",
        PlatformAccessibilityRole.ScrollView => "android.widget.ScrollView",
        _ => "android.view.View"
    };

    private sealed record DocumentState(PlatformAccessibilitySnapshot Snapshot,
        Dictionary<int, PlatformAccessibilityNode> Nodes, Func<int, PlatformAccessibilityAction, bool> PerformAction);

    private sealed class VirtualNodeProvider(AndroidAccessibilityAdapter owner) : AccessibilityNodeProvider
    {
        public override AccessibilityNodeInfo? CreateAccessibilityNodeInfo(int virtualViewId) => owner.CreateNode(virtualViewId);
        public override bool PerformAction(int virtualViewId, Action action, Bundle? arguments) =>
            owner.Perform(virtualViewId, action);
    }
}
