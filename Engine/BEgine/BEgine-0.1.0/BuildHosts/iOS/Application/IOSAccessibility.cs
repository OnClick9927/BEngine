using System;
using System.Collections.Generic;
using System.Linq;
using BEngine.Platform;
using CoreGraphics;
using Foundation;
using UIKit;

namespace BEngine.Player.Hosts.iOS;

internal sealed class IOSAccessibilityAdapter(PlayerView surface) : IPlatformAccessibilityAdapter
{
    private readonly Dictionary<(long Document, int Node), AccessibilityProxyView> _views = [];
    private readonly Dictionary<long, HashSet<(long Document, int Node)>> _documents = [];

    public bool IsScreenReaderActive => UIAccessibility.IsVoiceOverRunning;

    public void AttachWindow(nint nativeHandle) { }

    public void UpdateHierarchy(long documentId, PlatformAccessibilitySnapshot snapshot,
        Func<int, PlatformAccessibilityAction, bool> performAction)
    {
        var identities = snapshot.Nodes.Select(node => (documentId, node.Id)).ToHashSet();
        if (_documents.TryGetValue(documentId, out var previous))
            foreach (var identity in previous.Except(identities).ToArray()) Remove(identity);
        _documents[documentId] = identities;

        var nodes = snapshot.Nodes.ToDictionary(node => node.Id);
        foreach (var node in snapshot.Nodes)
        {
            var identity = (documentId, node.Id);
            if (!_views.TryGetValue(identity, out var view))
            {
                view = new AccessibilityProxyView(node.Id, performAction);
                _views.Add(identity, view);
            }
            view.Update(node, Scale(surface));
        }
        foreach (var node in snapshot.Nodes)
        {
            var view = _views[(documentId, node.Id)];
            UIView parent = node.ParentId is { } parentId && _views.TryGetValue((documentId, parentId), out var parentView)
                ? parentView : surface;
            if (!ReferenceEquals(view.Superview, parent))
            {
                view.RemoveFromSuperview();
                parent.AddSubview(view);
            }
            if (node.ParentId is { } id && nodes.TryGetValue(id, out var parentNode))
                view.Frame = RelativeFrame(node.Bounds, parentNode.Bounds, Scale(surface));
            else view.Frame = Frame(node.Bounds, Scale(surface));
        }
        UIAccessibility.PostNotification(UIAccessibilityPostNotification.LayoutChanged, null);
    }

    public void RemoveHierarchy(long documentId)
    {
        if (!_documents.Remove(documentId, out var identities)) return;
        foreach (var identity in identities) Remove(identity);
        UIAccessibility.PostNotification(UIAccessibilityPostNotification.LayoutChanged, null);
    }

    public void Notify(PlatformAccessibilityEvent accessibilityEvent)
    {
        if (accessibilityEvent.Kind == PlatformAccessibilityEventKind.Announcement &&
            !string.IsNullOrWhiteSpace(accessibilityEvent.Message))
        {
            using var message = new NSString(accessibilityEvent.Message);
            UIAccessibility.PostNotification(UIAccessibilityPostNotification.Announcement, message);
            return;
        }
        NSObject? argument = null;
        if (accessibilityEvent.NodeId is { } nodeId &&
            _views.TryGetValue((accessibilityEvent.DocumentId, nodeId), out var target)) argument = target;
        UIAccessibility.PostNotification(accessibilityEvent.Kind == PlatformAccessibilityEventKind.FocusChanged
            ? UIAccessibilityPostNotification.ScreenChanged : UIAccessibilityPostNotification.LayoutChanged, argument);
    }

    public void Dispose()
    {
        foreach (var identity in _views.Keys.ToArray()) Remove(identity);
        _documents.Clear();
    }

    private void Remove((long Document, int Node) identity)
    {
        if (!_views.Remove(identity, out var view)) return;
        view.RemoveFromSuperview();
        view.Dispose();
    }

    private static double Scale(UIView view) => Math.Max(1, view.ContentScaleFactor);
    private static CGRect Frame(PlatformAccessibilityBounds bounds, double scale) =>
        new(bounds.X / scale, bounds.Y / scale, Math.Max(1, bounds.Width / scale), Math.Max(1, bounds.Height / scale));
    private static CGRect RelativeFrame(PlatformAccessibilityBounds bounds, PlatformAccessibilityBounds parent, double scale) =>
        new((bounds.X - parent.X) / scale, (bounds.Y - parent.Y) / scale,
            Math.Max(1, bounds.Width / scale), Math.Max(1, bounds.Height / scale));

    private sealed class AccessibilityProxyView(int nodeId,
        Func<int, PlatformAccessibilityAction, bool> performAction) : UIView
    {
        internal void Update(PlatformAccessibilityNode node, double scale)
        {
            AccessibilityLabel = node.Name;
            AccessibilityHint = node.Description;
            AccessibilityValue = node.Value;
            AccessibilityTraits = Traits(node);
            IsAccessibilityElement = IsElement(node.Role);
            UserInteractionEnabled = false;
            BackgroundColor = UIColor.Clear;
            Frame = IOSAccessibilityAdapter.Frame(node.Bounds, scale);
        }

        public override bool AccessibilityActivate() => performAction(nodeId, PlatformAccessibilityAction.Activate);
        public override void AccessibilityIncrement() => performAction(nodeId, PlatformAccessibilityAction.Increment);
        public override void AccessibilityDecrement() => performAction(nodeId, PlatformAccessibilityAction.Decrement);

        private static bool IsElement(PlatformAccessibilityRole role) => role is not
            (PlatformAccessibilityRole.Group or PlatformAccessibilityRole.List or PlatformAccessibilityRole.ScrollView or
             PlatformAccessibilityRole.Menu);

        private static UIAccessibilityTrait Traits(PlatformAccessibilityNode node)
        {
            var traits = node.Role switch
            {
                PlatformAccessibilityRole.Button or PlatformAccessibilityRole.Toggle or
                    PlatformAccessibilityRole.RadioButton or PlatformAccessibilityRole.MenuItem => UIAccessibilityTrait.Button,
                PlatformAccessibilityRole.Link => UIAccessibilityTrait.Link,
                PlatformAccessibilityRole.Image => UIAccessibilityTrait.Image,
                PlatformAccessibilityRole.Heading => UIAccessibilityTrait.Header,
                PlatformAccessibilityRole.Text => UIAccessibilityTrait.StaticText,
                PlatformAccessibilityRole.SearchField => UIAccessibilityTrait.SearchField,
                PlatformAccessibilityRole.Slider => UIAccessibilityTrait.Adjustable,
                PlatformAccessibilityRole.ProgressBar => UIAccessibilityTrait.UpdatesFrequently,
                _ => UIAccessibilityTrait.None
            };
            if (node.State.HasFlag(PlatformAccessibilityState.Disabled)) traits |= UIAccessibilityTrait.NotEnabled;
            if (node.State.HasFlag(PlatformAccessibilityState.Selected) ||
                node.State.HasFlag(PlatformAccessibilityState.Checked)) traits |= UIAccessibilityTrait.Selected;
            return traits;
        }
    }
}
