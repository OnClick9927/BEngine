using BEngine.UIElements;
using System.Reflection;

namespace BEngine.Editor;

public class GUIContent
{
    public string text { get; set; }
    public string tooltip { get; set; }

    public GUIContent(string text = "", string tooltip = "")
    {
        this.text = text;
        this.tooltip = tooltip;
    }

    public static implicit operator GUIContent(string text) => new(text);
    public static GUIContent none { get; } = new();
}

public interface IHasCustomMenu
{
    void AddItemsToMenu(GenericMenu menu);
}

public abstract class EditorWindow : ScriptableObject, IHasCustomMenu
{
    private static readonly List<WeakReference<EditorWindow>> Windows = [];
    private bool _enabled;

    public static EditorWindow? focusedWindow { get; private set; }
    public static EditorWindow? mouseOverWindow { get; internal set; }

    public GUIContent titleContent { get; set; } = new();
    public Rect position { get; set; } = new(100, 100, 480, 320);
    public Vector2 minSize { get; set; } = new(160, 100);
    public Vector2 maxSize { get; set; } = new(8192, 8192);
    public bool wantsMouseMove { get; set; }
    public bool autoRepaintOnSceneChange { get; set; } = true;
    public bool docked { get; internal set; }
    public bool isLocked { get; set; }
    public VisualElement rootVisualElement { get; } = new();
    internal string PersistentId { get; set; } = string.Empty;
    internal bool IsOpen { get; private set; }
    internal bool WantsToFloat { get; private set; }

    protected EditorWindow() => titleContent = new GUIContent(GetType().Name.Replace("Window", string.Empty));

    public static T GetWindow<T>(string? title = null, bool focus = true) where T : EditorWindow, new()
    {
        var window = EnumerateWindows(openOnly: false).OfType<T>().FirstOrDefault() ?? CreateWindow<T>();
        if (!string.IsNullOrWhiteSpace(title)) window.titleContent = new GUIContent(title);
        window.Show();
        if (focus) window.Focus();
        return window;
    }

    public static EditorWindow GetWindow(Type windowType, bool utility = false, string? title = null,
        bool focus = true)
    {
        ArgumentNullException.ThrowIfNull(windowType);
        if (!typeof(EditorWindow).IsAssignableFrom(windowType) || windowType.IsAbstract)
        {
            throw new ArgumentException($"{windowType.FullName} is not a concrete EditorWindow type.",
                nameof(windowType));
        }
        var window = EnumerateWindows(openOnly: false).FirstOrDefault(item => item.GetType() == windowType) ??
                     (EditorWindow)ScriptableObject.CreateInstance(windowType);
        if (!string.IsNullOrWhiteSpace(title)) window.titleContent = new GUIContent(title);
        if (utility) window.ShowUtility();
        else window.Show();
        if (focus) window.Focus();
        return window;
    }

    public static T CreateWindow<T>() where T : EditorWindow, new() => ScriptableObject.CreateInstance<T>();
    public static bool HasOpenInstances<T>() where T : EditorWindow => EnumerateOpenWindows().OfType<T>().Any();

    public void Show() => EditorBridge.Host?.ShowWindow(this);
    public void ShowUtility() => ShowFloating();
    public void ShowAuxWindow() => ShowFloating();
    public void ShowPopup() => ShowFloating();
    public void ShowAsDropDown(Rect buttonRect, Vector2 windowSize)
    {
        position = new Rect(buttonRect.x, buttonRect.y + buttonRect.height, windowSize.x, windowSize.y);
        ShowPopup();
    }
    public void Close() => EditorBridge.Host?.CloseWindow(this);
    public void Repaint() => EditorBridge.Host?.RepaintWindow(this);
    public void Focus()
    {
        Show();
        FocusInternal();
    }

    protected virtual void OnEnable() { }
    protected virtual void OnDisable() { }
    protected virtual void OnFocus() { }
    protected virtual void OnLostFocus() { }
    protected virtual void OnHierarchyChange() { }
    protected virtual void OnProjectChange() { }
    protected virtual void OnSelectionChange() { }
    protected virtual void Update() { }
    protected virtual void CreateGUI() { }
    public virtual void AddItemsToMenu(GenericMenu menu) { }

    internal void OpenInternal()
    {
        if (IsOpen) return;
        IsOpen = true;
        Windows.Add(new WeakReference<EditorWindow>(this));
        if (!_enabled)
        {
            _enabled = true;
            OnEnable();
            CreateGUI();
        }
    }

    internal void CloseInternal()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (ReferenceEquals(focusedWindow, this))
        {
            focusedWindow = null;
            OnLostFocus();
        }
        if (_enabled)
        {
            _enabled = false;
            OnDisable();
        }
    }

    internal void UpdateInternal()
    {
        Update();
    }

    internal void PopulateContextMenu(GenericMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        AddItemsToMenu(menu);
        var contextMethods = GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                                  BindingFlags.NonPublic)
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<ContextMenuAttribute>()))
            .Where(item => item.Attribute is not null)
            .OrderBy(item => item.Attribute!.itemName, StringComparer.Ordinal)
            .ToArray();
        if (contextMethods.Length > 0 && menu.GetItemCount() > 0) menu.AddSeparator(string.Empty);
        foreach (var (method, attribute) in contextMethods)
        {
            if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
            {
                menu.AddDisabledItem(new GUIContent($"{attribute!.itemName} (Invalid)"));
                continue;
            }
            menu.AddItem(new GUIContent(attribute!.itemName), false, () => InvokeContextMethod(method));
        }
    }

    private void InvokeContextMethod(MethodInfo method)
    {
        try
        {
            method.Invoke(this, null);
        }
        catch (Exception exception)
        {
            var cause = exception is TargetInvocationException { InnerException: not null }
                ? exception.InnerException
                : exception;
            Debug.LogError($"Window command '{GetType().Name}.{method.Name}' failed: {cause.Message}");
        }
    }

    internal void FocusInternal()
    {
        if (ReferenceEquals(focusedWindow, this)) return;
        focusedWindow?.OnLostFocus();
        focusedWindow = this;
        OnFocus();
    }

    internal bool ConsumeFloatingRequest()
    {
        var requested = WantsToFloat;
        WantsToFloat = false;
        return requested;
    }

    private void ShowFloating()
    {
        WantsToFloat = true;
        Show();
    }

    internal static void NotifySelectionChanged()
    {
        foreach (var window in EnumerateOpenWindows()) window.OnSelectionChange();
    }

    internal static void NotifyHierarchyChanged()
    {
        foreach (var window in EnumerateOpenWindows()) window.OnHierarchyChange();
    }

    internal static void NotifyProjectChanged()
    {
        foreach (var window in EnumerateOpenWindows()) window.OnProjectChange();
    }

    internal static IEnumerable<EditorWindow> EnumerateOpenWindows() => EnumerateWindows(openOnly: true);

    private static IEnumerable<EditorWindow> EnumerateWindows(bool openOnly)
    {
        for (var index = Windows.Count - 1; index >= 0; index--)
        {
            if (!Windows[index].TryGetTarget(out var window))
            {
                Windows.RemoveAt(index);
                continue;
            }
            if (openOnly && !window.IsOpen) continue;
            yield return window;
        }
    }
}
