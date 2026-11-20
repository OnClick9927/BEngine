using BEngine.UIElements;

namespace BEngine.App.UI;

public enum UIPanelState { Created, Hidden, Visible, Closed }
public enum UIPanelMode { SingleInstance, MultipleInstances }

/// <summary>A managed UIElements panel. A factory creates a fresh panel and a detached visual root.
/// The owning UIService releases the panel; a hidden panel retains its resources until closed.</summary>
public abstract class UIPanel : IDisposable
{
    private readonly LifetimeScope _lifetime = new();
    internal UIService? Owner;
    public VisualElement Root { get; }
    public UIPanelState State { get; internal set; }
    public string Key { get; internal set; } = string.Empty;
    public int Layer { get; internal set; }
    public bool IsVisible => State == UIPanelState.Visible;
    protected LifetimeScope Lifetime => _lifetime;
    protected UIPanel(VisualElement? root = null) => Root = root ?? new VisualElement();
    protected T Track<T>(T resource) where T : IDisposable => _lifetime.Track(resource);
    protected virtual void OnLoad() { }
    protected virtual void OnShow(object? argument) { }
    protected virtual void OnHide() { }
    protected virtual void OnClose() { }
    internal void Load() => OnLoad();
    internal void Show(object? argument) => OnShow(argument);
    internal void Hide() => OnHide();
    internal void Release()
    {
        if (State == UIPanelState.Closed) return;
        State = UIPanelState.Closed;
        Owner = null;
        List<Exception>? failures = null;
        try { OnClose(); } catch (Exception error) { (failures ??= []).Add(error); }
        try { _lifetime.Dispose(); } catch (Exception error) { (failures ??= []).Add(error); }
        try { Root.RemoveFromHierarchy(); } catch (Exception error) { (failures ??= []).Add(error); }
        if (failures is not null) throw new AggregateException("UI panel cleanup failed.", failures);
    }
    public void Dispose()
    {
        if (Owner is { } owner) owner.Close(this);
        else Release();
    }
}
