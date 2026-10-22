using System.Globalization;
using System.Runtime.InteropServices;

namespace BEngine.Platform;

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class WindowsRootProvider(WindowsAccessibilityAdapter adapter) : IRawElementProviderFragmentRoot
{
    public UiaProviderOptions ProviderOptions => UiaProviderOptions.ServerSideProvider | UiaProviderOptions.UseComThreading;
    public object? GetPatternProvider(int patternId) => null;
    public object? GetPropertyValue(int propertyId) => propertyId switch
    {
        30003 => 50025, 30005 => "BEngine Player", 30010 => true, 30016 => true, 30017 => false,
        30020 => (int)adapter.Window, 30024 => "BEngine", 30012 => "BEnginePlayerWindow", _ => null
    };
    public IRawElementProviderSimple? HostRawElementProvider => adapter.HostProvider();
    public IRawElementProviderFragment? Navigate(UiaNavigateDirection direction)
    {
        var roots = adapter.Roots();
        return direction switch
        {
            UiaNavigateDirection.FirstChild when roots.Count > 0 => new WindowsNodeProvider(adapter, roots[0].DocumentId, roots[0].Node.Id),
            UiaNavigateDirection.LastChild when roots.Count > 0 => new WindowsNodeProvider(adapter, roots[^1].DocumentId, roots[^1].Node.Id),
            _ => null
        };
    }
    public int[] GetRuntimeId() => [42, Environment.ProcessId];
    public UiaRect BoundingRectangle => adapter.Roots().Select(root => adapter.ToScreen(root.Node.Bounds)).Aggregate(
        new UiaRect(), static (bounds, next) => bounds.Width == 0 && bounds.Height == 0 ? next : new(
            Math.Min(bounds.Left, next.Left), Math.Min(bounds.Top, next.Top),
            Math.Max(bounds.Left + bounds.Width, next.Left + next.Width) - Math.Min(bounds.Left, next.Left),
            Math.Max(bounds.Top + bounds.Height, next.Top + next.Height) - Math.Min(bounds.Top, next.Top)));
    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;
    public void SetFocus() { }
    public IRawElementProviderFragmentRoot FragmentRoot => this;
    public IRawElementProviderFragment? ElementProviderFromPoint(double x, double y)
    {
        foreach (var root in adapter.Roots().Reverse())
        {
            var hit = Find(root.DocumentId, root.Node.Id, x, y);
            if (hit is not null) return hit;
        }
        return null;
    }
    public IRawElementProviderFragment? GetFocus()
    {
        var focused = adapter.Roots().SelectMany(root => Descendants(root.DocumentId, root.Node.Id))
            .FirstOrDefault(node => node.Node.State.HasFlag(PlatformAccessibilityState.Focused));
        return focused.Node is null ? null : new WindowsNodeProvider(adapter, focused.DocumentId, focused.Node.Id);
    }

    private WindowsNodeProvider? Find(long documentId, int nodeId, double x, double y)
    {
        foreach (var child in adapter.Children(documentId, nodeId).Reverse())
        {
            var hit = Find(documentId, child.Id, x, y); if (hit is not null) return hit;
        }
        var node = adapter.Node(documentId, nodeId); if (node is null) return null;
        var bounds = adapter.ToScreen(node.Bounds);
        return x >= bounds.Left && y >= bounds.Top && x <= bounds.Left + bounds.Width && y <= bounds.Top + bounds.Height
            ? new WindowsNodeProvider(adapter, documentId, nodeId) : null;
    }
    private IEnumerable<(long DocumentId, PlatformAccessibilityNode Node)> Descendants(long documentId, int nodeId)
    {
        if (adapter.Node(documentId, nodeId) is { } node) yield return (documentId, node);
        foreach (var child in adapter.Children(documentId, nodeId))
            foreach (var descendant in Descendants(documentId, child.Id)) yield return descendant;
    }
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class WindowsNodeProvider(WindowsAccessibilityAdapter adapter, long documentId, int nodeId) :
    IRawElementProviderFragment, IInvokeProvider, IToggleProvider, IRangeValueProvider
{
    private PlatformAccessibilityNode Node => adapter.Node(documentId, nodeId) ??
        throw new InvalidOperationException("The accessibility node no longer exists.");
    public UiaProviderOptions ProviderOptions => UiaProviderOptions.ServerSideProvider | UiaProviderOptions.UseComThreading;
    public object? GetPatternProvider(int patternId) => patternId switch
    {
        10000 when Node.Role is PlatformAccessibilityRole.Button or PlatformAccessibilityRole.Link or PlatformAccessibilityRole.MenuItem => this,
        10015 when Node.Role is PlatformAccessibilityRole.Toggle or PlatformAccessibilityRole.RadioButton => this,
        10003 when Node.Role == PlatformAccessibilityRole.Slider => this,
        _ => null
    };
    public object? GetPropertyValue(int propertyId) => propertyId switch
    {
        30003 => ControlType(Node.Role), 30005 => Node.Name, 30008 => Node.State.HasFlag(PlatformAccessibilityState.Focused),
        30009 => IsInteractive(Node.Role), 30010 => !Node.State.HasFlag(PlatformAccessibilityState.Disabled),
        30011 => $"bengine-{documentId}-{nodeId}", 30012 => Node.Role.ToString(), 30013 => Node.Description,
        30016 => true, 30017 => true, 30019 => Node.State.HasFlag(PlatformAccessibilityState.Password),
        30022 => Node.Bounds.Width <= 0 || Node.Bounds.Height <= 0, 30024 => "BEngine", 30026 => Node.Value, _ => null
    };
    public IRawElementProviderSimple? HostRawElementProvider => null;
    public IRawElementProviderFragment? Navigate(UiaNavigateDirection direction)
    {
        var node = Node;
        if (direction == UiaNavigateDirection.Parent)
            return node.ParentId is { } parent ? new WindowsNodeProvider(adapter, documentId, parent) : new WindowsRootProvider(adapter);
        var children = adapter.Children(documentId, node.Id);
        if (direction == UiaNavigateDirection.FirstChild && children.Count > 0) return new WindowsNodeProvider(adapter, documentId, children[0].Id);
        if (direction == UiaNavigateDirection.LastChild && children.Count > 0) return new WindowsNodeProvider(adapter, documentId, children[^1].Id);
        var siblings = adapter.Children(documentId, node.ParentId);
        var index = siblings.ToList().FindIndex(item => item.Id == node.Id);
        if (direction == UiaNavigateDirection.NextSibling && index >= 0 && index + 1 < siblings.Count)
            return new WindowsNodeProvider(adapter, documentId, siblings[index + 1].Id);
        if (direction == UiaNavigateDirection.PreviousSibling && index > 0)
            return new WindowsNodeProvider(adapter, documentId, siblings[index - 1].Id);
        return null;
    }
    public int[] GetRuntimeId() => [42, unchecked((int)documentId), nodeId];
    public UiaRect BoundingRectangle => adapter.ToScreen(Node.Bounds);
    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;
    public void SetFocus() => adapter.Perform(documentId, nodeId, PlatformAccessibilityAction.Focus);
    public IRawElementProviderFragmentRoot FragmentRoot => new WindowsRootProvider(adapter);
    public void Invoke() => adapter.Perform(documentId, nodeId, PlatformAccessibilityAction.Activate);
    public void Toggle() => adapter.Perform(documentId, nodeId, PlatformAccessibilityAction.Activate);
    public UiaToggleState ToggleState => Node.State.HasFlag(PlatformAccessibilityState.Checked) ? UiaToggleState.On : UiaToggleState.Off;
    public void SetValue(double value)
    {
        var current = double.TryParse(Node.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : Minimum;
        var action = value >= current ? PlatformAccessibilityAction.Increment : PlatformAccessibilityAction.Decrement;
        var steps = Math.Clamp((int)Math.Ceiling(Math.Abs(value - current) / Math.Max(SmallChange, double.Epsilon)), 1, 1000);
        for (var i = 0; i < steps; i++) adapter.Perform(documentId, nodeId, action);
    }
    public double Value => double.TryParse(Node.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
    public bool IsReadOnly => Node.State.HasFlag(PlatformAccessibilityState.ReadOnly);
    public double Maximum => Node.MaximumValue ?? 0;
    public double Minimum => Node.MinimumValue ?? 0;
    public double LargeChange => Math.Max(SmallChange, (Maximum - Minimum) / 10);
    public double SmallChange => Math.Max(double.Epsilon, (Maximum - Minimum) / 100);

    private static bool IsInteractive(PlatformAccessibilityRole role) => role is PlatformAccessibilityRole.Button or
        PlatformAccessibilityRole.Toggle or PlatformAccessibilityRole.RadioButton or PlatformAccessibilityRole.TextField or
        PlatformAccessibilityRole.SearchField or PlatformAccessibilityRole.Slider or PlatformAccessibilityRole.ComboBox or
        PlatformAccessibilityRole.MenuItem or PlatformAccessibilityRole.Link;
    private static int ControlType(PlatformAccessibilityRole role) => role switch
    {
        PlatformAccessibilityRole.Button => 50000, PlatformAccessibilityRole.Toggle => 50002,
        PlatformAccessibilityRole.ComboBox => 50003, PlatformAccessibilityRole.TextField or PlatformAccessibilityRole.SearchField => 50004,
        PlatformAccessibilityRole.Link => 50005, PlatformAccessibilityRole.Image => 50006,
        PlatformAccessibilityRole.ListItem => 50007, PlatformAccessibilityRole.List => 50008,
        PlatformAccessibilityRole.Menu => 50009, PlatformAccessibilityRole.MenuItem => 50011,
        PlatformAccessibilityRole.ProgressBar => 50012, PlatformAccessibilityRole.RadioButton => 50013,
        PlatformAccessibilityRole.ScrollView => 50014, PlatformAccessibilityRole.Slider => 50015,
        PlatformAccessibilityRole.Text or PlatformAccessibilityRole.Heading => 50020,
        PlatformAccessibilityRole.Group => 50026, _ => 50025
    };
}
