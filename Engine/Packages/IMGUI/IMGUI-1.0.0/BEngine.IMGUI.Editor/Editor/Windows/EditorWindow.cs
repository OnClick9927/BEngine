using System.Reflection;
using System.Linq.Expressions;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

/// <summary>
/// An editor surface hosted by the engine dock, a transient in-process layer, or an independent
/// native window while floating. Native floating windows are pumped by the editor thread.
/// </summary>
public abstract class EditorWindow : ScriptableObject, IEditorView, IHasCustomMenu, IEditorWindowView,
    IEditorGraphicsViewport
{
    private static readonly List<WeakReference<EditorWindow>> Windows = [];
    private readonly string _windowId = Guid.NewGuid().ToString("N");
    private IEditorWindowModel? _model;
    private IEditorWindowController? _controller;
    private bool _enabled;
    private bool _isLocked;
    private double _lastInspectorUpdate;
    protected IEditorWindowContent? content { get; set; }
    public static EditorWindow? focusedWindow { get; private set; }
    public static EditorWindow? mouseOverWindow { get; internal set; }
    internal static EditorWindow? currentDrawingWindow { get; private set; }
    internal virtual bool dismissPopupOnFocusLost => true;

    public GUIContent titleContent
    {
        get;
        set
        {
            field = value ?? GUIContent.none;
            EditorCallbacks.Dispatch(titleContentChanged, this, nameof(titleContentChanged));
        }
    } = new();
    public Rect position { get; set; } = new(100, 100, 480, 320);
    public Vector2 minSize { get; set; } = new(160, 100);
    public Vector2 maxSize { get; set; } = new(8192, 8192);
    public bool wantsMouseMove { get; set; }
    public bool wantsMouseEnterLeaveWindow { get; set; }
    public EventInterests eventInterests { get; set; }
    public bool hasFocus => ReferenceEquals(focusedWindow, this);
    public bool autoRepaintOnSceneChange { get; set; } = true;
    public string windowId => _windowId;
    public bool isOpen => IsOpen;
    public IEditorWindowModel model
    {
        get
        {
            EnsureMvc();
            return _model!;
        }
    }
    public IEditorWindowController controller
    {
        get
        {
            EnsureMvc();
            return _controller!;
        }
    }
    public bool saveToLayout { get; protected set; } = true;
    public bool docked { get; internal set; }
    public bool isLocked
    {
        get => _isLocked;
        set
        {
            if (_isLocked == value) return;
            _isLocked = value;
            if (_enabled) InvokeCallback(OnLockStateChanged, nameof(OnLockStateChanged));
            Repaint();
        }
    }
    public EditorWindowState windowState { get; internal set; } = EditorWindowState.Normal;
    internal string PersistentId { get; set; } = string.Empty;
    internal bool IsOpen { get; private set; }
    internal EditorWindowState? RequestedState { get; private set; }
    internal bool RequestedFocus { get; private set; } = true;
    internal event Action<EditorWindow>? titleContentChanged;

    protected EditorWindow()
    {
        var conventionalIcon = $"Icons/Windows/{GetType().Name.Replace("Window", string.Empty)}.png";
        var icon = EditorWindowMetadataRegistry.GetIcon(GetType()) ??
                   (EditorResource.FindPath(conventionalIcon) is not null
                       ? conventionalIcon
                       : "Icons/Windows/Window.png");
        titleContent = new GUIContent(GetType().Name.Replace("Window", string.Empty), icon,
            GetType().FullName ?? string.Empty);
        EditorWindowRegistry.Register(this);
    }

    public static T GetWindow<T>(string? title = null, bool focus = true) where T : EditorWindow, new()
    {
        var window = EnumerateWindows(openOnly: false).OfType<T>().FirstOrDefault() ?? CreateWindow<T>();
        if (!string.IsNullOrWhiteSpace(title)) window.titleContent = new GUIContent(title,
            window.titleContent.image, window.titleContent.tooltip);
        window.RequestState(EditorWindowState.Normal, focus);
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
        var window = Matches4488() ?? (EditorWindow)ScriptableObject.CreateInstance(windowType);
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Editor.EditorWindow? Matches4488()
        {
            var matchSource4488 = EnumerateWindows(openOnly: false);
            ArgumentNullException.ThrowIfNull(matchSource4488, "source");
            global::System.ReadOnlySpan<global::BEngine.Editor.EditorWindow> matchValues4488;
            if (matchSource4488.GetType() == typeof(global::BEngine.Editor.EditorWindow[]))
            {
                matchValues4488 = (global::BEngine.Editor.EditorWindow[])(object)matchSource4488;
            }
            else if (matchSource4488.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Editor.EditorWindow>))
            {
                matchValues4488 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Editor.EditorWindow>)(object)matchSource4488);
            }
            else
            {
                foreach (var item in (global::System.Collections.Generic.IEnumerable<global::BEngine.Editor.EditorWindow>)matchSource4488)
                {
                    if (item.GetType() == windowType)
                        return item;
                }
        
                return default!;
            }
        
            foreach (var item in matchValues4488)
            {
                if (item.GetType() == windowType)
                    return item;
            }
        
            return default!;
        }
        if (!string.IsNullOrWhiteSpace(title)) window.titleContent = new GUIContent(title,
            window.titleContent.image, window.titleContent.tooltip);
        window.RequestState(utility ? EditorWindowState.Aux : EditorWindowState.Normal, focus);
        return window;
    }

    public static T CreateWindow<T>() where T : EditorWindow, new() => ScriptableObject.CreateInstance<T>();
    public static bool HasOpenInstances<T>() where T : EditorWindow => EnumerateOpenWindows().OfType<T>().Any();

    public void Show() => RequestState(EditorWindowState.Normal, focus: true);
    public void ShowUtility() => RequestState(EditorWindowState.Aux, focus: true);
    public void ShowAuxWindow() => RequestState(EditorWindowState.Aux, focus: true);
    public void ShowPopup() => RequestState(EditorWindowState.Pop, focus: true);
    public void ShowModal() => RequestState(EditorWindowState.Modal, focus: true);
    public void ShowModalUtility() => RequestState(EditorWindowState.Modal, focus: true);
    public void ShowAsDropDown(Rect buttonRect, Vector2 windowSize)
    {
        var root = GUI.GUIToRootPoint(new Vector2(buttonRect.x, buttonRect.y + buttonRect.height));
        position = new Rect(root.x, root.y, windowSize.x, windowSize.y);
        ShowPopup();
    }
    public void Close()
    {
        if (EditorBridge.Host is { } host) host.CloseWindow(this);
        else CloseInternal();
    }
    public void Repaint() => EditorBridge.Host?.RepaintWindow(this);
    public void Focus()
    {
        if (!IsOpen)
        {
            RequestState(EditorWindowState.Normal, focus: true);
            return;
        }
        if (EditorBridge.Host is { } host) host.FocusWindow(this);
        else FocusInternal();
    }

    protected virtual void OnEnable() { }
    protected virtual void OnDisable() { }
    protected virtual void OnFocus() { }
    protected virtual void OnLostFocus() { }
    protected virtual void OnHierarchyChange() { }
    protected virtual void OnProjectChange() { }
    protected virtual void OnSelectionChange() { }
    protected virtual void OnBecameVisible() { }
    protected virtual void OnBecameInvisible() { }
    protected virtual void OnInspectorUpdate() { }
    protected virtual void OnLockStateChanged() { }
    protected virtual void Update() { }
    protected virtual void OnGUI() { }
    public virtual void AddItemsToMenu(GenericMenu menu) { }

    /// <summary>Creates this window instance's model. Override to expose a strongly typed model.</summary>
    protected virtual IEditorWindowModel CreateModel(EditorWindowMvcContext context) =>
        new EditorWindowModel(context.WindowId);

    /// <summary>Creates this window instance's controller. Override to expose a strongly typed controller.</summary>
    protected virtual IEditorWindowController CreateController(
        EditorWindowMvcContext context,
        IEditorWindowModel windowModel) => new EditorWindowController(context, windowModel);

    internal void OpenInternal()
    {
        if (IsOpen) return;
        EnsureMvc();
        IsOpen = true;
        bool __queryResult7756;
        {
            var __querySource7756 = EnumerateWindows(openOnly: false);
            ArgumentNullException.ThrowIfNull(__querySource7756, "source");
            __queryResult7756 = false;
            var __querySource7756Type = __querySource7756.GetType();
            if (__querySource7756Type == typeof(BEngine.Editor.EditorWindow[]) || __querySource7756Type == typeof(System.Collections.Generic.List<BEngine.Editor.EditorWindow>))
            {
                System.ReadOnlySpan<BEngine.Editor.EditorWindow> __querySource7756Span = __querySource7756Type == typeof(BEngine.Editor.EditorWindow[]) ? (BEngine.Editor.EditorWindow[])(object)__querySource7756 : System.Runtime.InteropServices.CollectionsMarshal.AsSpan((System.Collections.Generic.List<BEngine.Editor.EditorWindow>)(object)__querySource7756);
                foreach (var __queryItem7756 in __querySource7756Span)
                {
                    if ((ReferenceEquals(__queryItem7756, this)))
                    {
                        __queryResult7756 = true;
                        break;
                    }
                }
            }
            else
            {
                foreach (var __queryItem7756 in __querySource7756)
                {
                    if ((ReferenceEquals(__queryItem7756, this)))
                    {
                        __queryResult7756 = true;
                        break;
                    }
                }
            }
        }
        
        if (!__queryResult7756)
            Windows.Add(new WeakReference<EditorWindow>(this));
        if (!_enabled)
        {
            _enabled = true;
            InvokeCallback(OnEnable, nameof(OnEnable));
            if (_isLocked) InvokeCallback(OnLockStateChanged, nameof(OnLockStateChanged));
        }
        _lastInspectorUpdate = EditorApplication.timeSinceStartup;
        InvokeCallback(OnBecameVisible, nameof(OnBecameVisible));
    }

    internal virtual bool supportsLocking => false;

    internal virtual string? CaptureLockContext() => null;

    internal virtual void RestoreLockContext(string? context) { }

    internal void CloseInternal()
    {
        if (!IsOpen) return;
        IsOpen = false;
        InvokeCallback(OnBecameInvisible, nameof(OnBecameInvisible));
        if (ReferenceEquals(focusedWindow, this))
        {
            focusedWindow = null;
            GUIUtility.ReleaseInputFocus();
            InvokeCallback(OnLostFocus, nameof(OnLostFocus));
        }
        if (ReferenceEquals(mouseOverWindow, this)) SetMouseOverWindow(null);
        if (_enabled)
        {
            _enabled = false;
            InvokeCallback(OnDisable, nameof(OnDisable));
        }
        content?.ReleaseGraphicsDevices();
    }

    protected override void ReleaseAssetResources()
    {
        Close();
        CloseInternal();
        content?.ReleaseGraphicsDevices();
    }

    internal void UpdateInternal()
    {
        using (EditorInstrumentation.BeginSample(GetType(), nameof(Update)))
            InvokeCallback(Update, nameof(Update));
        var now = EditorApplication.timeSinceStartup;
        if (now - _lastInspectorUpdate < 0.1) return;
        _lastInspectorUpdate = now;
        using (EditorInstrumentation.BeginSample(GetType(), nameof(OnInspectorUpdate)))
            InvokeCallback(OnInspectorUpdate, nameof(OnInspectorUpdate));
    }

    internal void OnGUIInternal()
    {
        var previous = currentDrawingWindow;
        currentDrawingWindow = this;
        try
        {
            content?.SynchronizeLayout(
                GUIUtility.currentViewWidth, GUIUtility.currentViewHeight);
            using (EditorInstrumentation.BeginSample(GetType(), nameof(OnGUI)))
                InvokeCallback(OnGUI, nameof(OnGUI));
            content?.HandleEvent(Event.current);
        }
        finally
        {
            currentDrawingWindow = previous;
        }
    }

    bool IEditorGraphicsViewport.TryGetGraphicsViewport(out EditorGraphicsViewport viewport) =>
        TryGetContentViewport(out viewport);

    private bool TryGetContentViewport(out EditorGraphicsViewport viewport)
    {
        viewport = default;
        return content is not null && content.TryGetGraphicsViewport(out viewport);
    }

    void IEditorGraphicsViewport.RenderGraphicsViewport(
        IGraphicsDevice device,
        GraphicsRect viewport,
        int targetWidth,
        int targetHeight) =>
        content?.RenderGraphicsViewport(device, viewport, targetWidth, targetHeight);

    void IEditorGraphicsViewport.ReleaseGraphicsDevice(IGraphicsDevice device) =>
        content?.ReleaseGraphicsDevice(device);

    internal void PopulateContextMenu(GenericMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        InvokeCallback(() => AddItemsToMenu(menu), nameof(AddItemsToMenu));
        var contextMethods = EditorWindowMetadataRegistry.GetContextCommands(GetType());
        if (contextMethods.Length > 0 && menu.GetItemCount() > 0) menu.AddSeparator(string.Empty);
        foreach (var command in contextMethods)
        {
            menu.AddItem(new GUIContent(command.Name), false, () => InvokeContextCommand(command));
        }
    }

    private void InvokeContextCommand(EditorWindowContextCommand command)
    {
        EditorCallbacks.Invoke(this, command.MethodName, () => command.Callback(this));
    }

    internal void FocusInternal()
    {
        if (ReferenceEquals(focusedWindow, this)) return;
        var previous = focusedWindow;
        GUIUtility.ReleaseInputFocus();
        previous?.InvokeCallback(previous.OnLostFocus, nameof(OnLostFocus));
        focusedWindow = this;
        InvokeCallback(OnFocus, nameof(OnFocus));
    }

    internal void LoseFocusInternal()
    {
        if (!ReferenceEquals(focusedWindow, this)) return;
        focusedWindow = null;
        GUIUtility.ReleaseInputFocus();
        InvokeCallback(OnLostFocus, nameof(OnLostFocus));
    }

    internal static void SetMouseOverWindow(EditorWindow? window)
    {
        if (ReferenceEquals(mouseOverWindow, window)) return;
        mouseOverWindow = window;
    }

    internal EditorWindowState ConsumeRequestedState()
    {
        var requested = RequestedState ?? windowState;
        RequestedState = null;
        return requested;
    }

    internal bool ConsumeRequestedFocus()
    {
        var requested = RequestedFocus;
        RequestedFocus = true;
        return requested;
    }

    private void RequestState(EditorWindowState state, bool focus)
    {
        ObjectDisposedException.ThrowIf(isDestroyed, this);
        RequestedState = state;
        RequestedFocus = focus;
        if (EditorBridge.Host is { } host) host.ShowWindow(this);
        else { OpenInternal(); if (focus) FocusInternal(); }
    }

    internal static void NotifySelectionChanged()
    {
        foreach (var window in EnumerateOpenWindows())
            window.InvokeCallback(window.OnSelectionChange, nameof(OnSelectionChange));
    }

    internal static void NotifyHierarchyChanged()
    {
        foreach (var window in EnumerateOpenWindows())
            window.InvokeCallback(window.OnHierarchyChange, nameof(OnHierarchyChange));
    }

    internal static void NotifyProjectChanged()
    {
        foreach (var window in EnumerateOpenWindows())
            window.InvokeCallback(window.OnProjectChange, nameof(OnProjectChange));
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

    private void InvokeCallback(Action callback, string callbackName)
    {
        try { EditorCallbacks.Invoke(this, callbackName, callback); }
        catch (ExitGUIException) { }
    }

    private void EnsureMvc()
    {
        if (_model is not null && _controller is not null) return;
        var context = new EditorWindowMvcContext(windowId, this);
        var createdModel = CreateModel(context) ??
                           throw new InvalidOperationException(
                               $"{GetType().FullName}.CreateModel returned null.");
        if (!string.Equals(createdModel.WindowId, windowId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{GetType().FullName} created a model for a different EditorWindow id.");
        var createdController = CreateController(context, createdModel) ??
                                throw new InvalidOperationException(
                                    $"{GetType().FullName}.CreateController returned null.");
        if (!string.Equals(createdController.WindowId, windowId, StringComparison.Ordinal) ||
            !ReferenceEquals(createdController.Model, createdModel) ||
            !ReferenceEquals(createdController.View, this))
            throw new InvalidOperationException(
                $"{GetType().FullName} created a controller with an invalid MVC binding.");
        _model = createdModel;
        _controller = createdController;
    }
}
