using BEngine.UIElements;

namespace BEngine.App.UI;

/// <summary>One game-loop UI workflow over a borrowed UIElements root/document. Registration is explicit;
/// loading assets, building documents and platform rendering remain the caller's/UIElements' responsibility.
/// Several services may manage independent roots in the same GameDomain. Lifecycle callbacks must not
/// recursively mutate this service; domain shutdown/Dispose is drained after the current operation.</summary>
public sealed class UIService : GameService
{
    private readonly VisualElement _root;
    private readonly UIDocument? _document;
    private readonly Dictionary<string, Registration> _registrations = new(StringComparer.Ordinal);
    private readonly SortedDictionary<int, VisualElement> _layers = [];
    private readonly List<UIPanel> _panels = [];
    private readonly IReadOnlyList<UIPanel> _view;
    private bool _changing, _disposed, _disposePending;

    public VisualElement Root => _root;
    public IReadOnlyList<UIPanel> Panels => _view;
    public UIService(VisualElement root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _view = _panels.AsReadOnly();
    }
    public UIService(UIDocument document) : this((document ?? throw new ArgumentNullException(nameof(document))).rootVisualElement)
        => _document = document;

    /// <summary>SingleInstance re-shows the existing instance, including a hidden one. MultipleInstances
    /// creates a new panel on each Show; use the returned instance for Hide/Close.</summary>
    public void Register<T>(string key, Func<T> factory, int layer = 0,
        UIPanelMode mode = UIPanelMode.SingleInstance) where T : UIPanel
    {
        using var operation = BeginChange();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!_registrations.TryAdd(key, new Registration(() => factory(), layer, mode)))
            throw new InvalidOperationException($"UI panel '{key}' is already registered.");
    }

    public UIPanel Show(string key, object? argument = null)
    {
        using var operation = BeginChange();
        if (!_registrations.TryGetValue(key, out var registration))
            throw new KeyNotFoundException($"UI panel '{key}' is not registered.");
        if (registration.Mode == UIPanelMode.SingleInstance && FindCore(key) is { } existing)
        {
            ShowCore(existing, argument);
            return existing;
        }
        var panel = registration.Factory() ?? throw new InvalidOperationException("UI panel factory returned null.");
        if (panel.State != UIPanelState.Created || panel.Owner is not null || panel.Root.parent is not null ||
            ReferenceEquals(panel.Root, _root) || IsAncestor(panel.Root, _root))
            throw new InvalidOperationException("A UI factory must return a fresh panel with its own detached root.");
        panel.Key = key;
        panel.Layer = registration.Layer;
        panel.Owner = this;
        _panels.Add(panel);
        try
        {
            CheckAvailable();
            GetLayer(panel.Layer).Add(panel.Root);
            panel.Root.visible = false;
            panel.State = UIPanelState.Hidden;
            panel.Load();
            CheckAvailable();
            ShowCore(panel, argument);
            return panel;
        }
        catch (Exception error)
        {
            _panels.Remove(panel);
            try { panel.Release(); }
            catch (Exception cleanup) { throw new AggregateException("UI panel load/show and cleanup failed.", error, cleanup); }
            throw;
        }
    }

    public void Show(UIPanel panel, object? argument = null)
    {
        using var operation = BeginChange();
        CheckOwned(panel);
        ShowCore(panel, argument);
    }
    public UIPanel? Find(string key) { CheckAvailable(); return FindCore(key); }
    private UIPanel? FindCore(string key)
    {
        for (var i = _panels.Count - 1; i >= 0; i--)
            if (string.Equals(_panels[i].Key, key, StringComparison.Ordinal)) return _panels[i];
        return null;
    }
    public UIPanel? GetTopVisible(int? layer = null)
    {
        CheckAvailable();
        UIPanel? top = null;
        for (var i = 0; i < _panels.Count; i++)
        {
            var panel = _panels[i];
            if (panel.IsVisible && (layer is null || panel.Layer == layer) &&
                (top is null || panel.Layer >= top.Layer)) top = panel;
        }
        return top;
    }
    public bool Hide(UIPanel panel)
    {
        using var operation = BeginChange();
        CheckOwned(panel);
        if (!panel.IsVisible) return false;
        panel.State = UIPanelState.Hidden;
        panel.Root.visible = false;
        panel.Hide();
        return true;
    }
    public bool Close(UIPanel panel)
    {
        using var operation = BeginChange();
        if (panel is { State: UIPanelState.Closed }) return false;
        CheckOwned(panel);
        CloseCore(panel);
        return true;
    }
    public void CloseAll(int? layer = null)
    {
        using var operation = BeginChange();
        CloseAllCore(layer);
    }

    private void ShowCore(UIPanel panel, object? argument)
    {
        var previousState = panel.State;
        try { panel.Show(argument); CheckAvailable(); }
        catch
        {
            panel.Root.visible = previousState == UIPanelState.Visible;
            throw;
        }
        panel.Root.visible = true;
        panel.State = UIPanelState.Visible;
        // Bring to front inside its layer without changing relative layer order.
        // A stable front panel is already in the correct place; reattaching it invalidates layout/styles.
        var layer = GetLayer(panel.Layer);
        var children = layer.Children;
        if (children.Count == 0 || !ReferenceEquals(children[^1], panel.Root)) layer.Add(panel.Root);
        if (!ReferenceEquals(_panels[^1], panel))
        {
            _panels.Remove(panel);
            _panels.Add(panel);
        }
    }
    private void CloseCore(UIPanel panel)
    {
        _panels.Remove(panel);
        List<Exception>? failures = null;
        if (panel.IsVisible)
        {
            panel.State = UIPanelState.Hidden;
            panel.Root.visible = false;
            try { panel.Hide(); } catch (Exception error) { (failures ??= []).Add(error); }
        }
        try { panel.Release(); } catch (Exception error) { (failures ??= []).Add(error); }
        if (failures is not null) throw new AggregateException("UI panel close failed.", failures);
    }
    private void CloseAllCore(int? layer)
    {
        List<Exception>? failures = null;
        for (var i = _panels.Count - 1; i >= 0; i--)
        {
            var panel = _panels[i];
            if (layer is not null && panel.Layer != layer) continue;
            try { CloseCore(panel); } catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (failures is not null) throw new AggregateException("UI panels could not all be closed.", failures);
    }
    protected override void OnDispose()
    {
        if (_disposed) return;
        // Domain replacement may happen from inside a UI callback. Complete the current operation
        // before releasing its panel list, even though the base lifetime is already cancelled.
        if (_changing) { _disposePending = true; return; }
        DisposeCore();
    }
    private void DisposeCore()
    {
        _changing = true;
        _disposed = true;
        List<Exception>? failures = null;
        try { CloseAllCore(null); } catch (Exception error) { (failures ??= []).Add(error); }
        foreach (var layer in _layers.Values)
            try { layer.RemoveFromHierarchy(); } catch (Exception error) { (failures ??= []).Add(error); }
        _layers.Clear(); _registrations.Clear();
        _changing = false;
        if (failures is not null) throw new AggregateException("UI service cleanup failed.", failures);
    }
    private VisualElement GetLayer(int index)
    {
        if (_layers.TryGetValue(index, out var layer)) return layer;
        layer = new VisualElement { name = $"Application UI Layer {index}" };
        layer.style.position = Position.Absolute;
        layer.style.left = 0; layer.style.top = 0; layer.style.right = 0; layer.style.bottom = 0;
        _layers.Add(index, layer);
        var childIndex = 0;
        foreach (var pair in _layers)
        {
            if (pair.Key == index) break;
            childIndex++;
        }
        _root.Insert(childIndex, layer);
        return layer;
    }
    private void CheckOwned(UIPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (!ReferenceEquals(panel.Owner, this)) throw new InvalidOperationException("The panel is not owned by this UI service.");
    }
    private void CheckAvailable()
    {
        ObjectDisposedException.ThrowIf(_disposed || IsDisposed, this);
        if (_document is { } document && (document.isDestroyed || !ReferenceEquals(document.rootVisualElement, _root)))
            throw new InvalidOperationException("The UI document was destroyed or reloaded. Create a new UI service for its new root.");
    }
    private Operation BeginChange()
    {
        CheckAvailable();
        if (_changing) throw new InvalidOperationException("UI lifecycle callbacks cannot recursively change the same service.");
        _changing = true;
        return new Operation(this);
    }
    private static bool IsAncestor(VisualElement possibleAncestor, VisualElement element)
    {
        for (var current = element.parent; current is not null; current = current.parent)
            if (ReferenceEquals(current, possibleAncestor)) return true;
        return false;
    }
    private readonly record struct Registration(Func<UIPanel> Factory, int Layer, UIPanelMode Mode);
    private readonly struct Operation(UIService service) : IDisposable
    {
        public void Dispose()
        {
            service._changing = false;
            if (service._disposePending) { service._disposePending = false; service.DisposeCore(); }
        }
    }
}
