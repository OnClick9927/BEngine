using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

/// <summary>Hosts one floating EditorWindow in an independent OS window on the editor thread.</summary>
internal sealed class NativeFloatingEditorWindow : IDisposable
{
    private readonly ImGuiNativeWindow _nativeWindow;
    private readonly EditorWindowLayer _transientLayer = new();
    private readonly ImGuiPopupMenu _genericMenuPopup = new();
    private readonly ImGuiAdvancedDropdown _advancedDropdown = new();
    private readonly ImGuiObjectPicker _objectPicker = new();
    private Win32NativeMoveScope? _nativeMoveScope;
    private Rect? _genericMenuAnchor;
    private bool _disposed;
    private Action<GenericMenu, EditorWindow>? _populateAddNewTabMenu;

    internal NativeFloatingEditorWindow(EditorWindow window, EditorWindowState state, Rect screenBounds)
    {
        ArgumentNullException.ThrowIfNull(window);
        Window = window;
        State = state;
        var (minimumSize, maximumSize) = ClientSizeLimits();
        screenBounds = NativeFloatingWindowGeometry.ConstrainSize(screenBounds, minimumSize, maximumSize);
        screenBounds = NativeFloatingWindowGeometry.RestoreToVisibleWorkArea(screenBounds,
            NativeFloatingWindowGeometry.CurrentWorkAreas());
        var width = Math.Max(1, (int)screenBounds.width);
        var height = Math.Max(1, (int)screenBounds.height);
        // Floats are pumped serially on the editor thread. Per-window VSync would block once for
        // every visible float instead of once for the editor frame.
        _nativeWindow = new ImGuiNativeWindow(WindowTitle(window), width, height, vsync: false,
            minimumWidth: CeilingToInt(minimumSize.x), minimumHeight: CeilingToInt(minimumSize.y),
            maximumWidth: FloorToInt(maximumSize.x), maximumHeight: FloorToInt(maximumSize.y));
        _nativeWindow.Move((int)screenBounds.x, (int)screenBounds.y);
        window.position = screenBounds;
        _nativeWindow.gui += OnGUI;
        _nativeWindow.closing += OnNativeClosing;
        _nativeWindow.focusChanged += OnNativeFocusChanged;
        _transientLayer.WindowClosed += OnTransientWindowClosed;
        window.titleContentChanged += OnTitleContentChanged;
    }

    internal event Action<NativeFloatingEditorWindow>? CloseRequested;
    internal event Action<NativeFloatingEditorWindow, Vector2>? DockRequested;
    internal event Action<NativeFloatingEditorWindow, bool>? FocusChanged;
    internal event Action<NativeFloatingEditorWindow, EditorWindow>? TransientClosed;
    internal event Action<NativeFloatingEditorWindow, Vector2>? NativeMoveUpdated;
    internal event Action<NativeFloatingEditorWindow, Vector2?>? NativeMoveCompleted;

    internal EditorWindow Window { get; }
    internal EditorWindowState State { get; }
    internal bool IsFocused => _nativeWindow.isFocused;
    internal bool IsClosing => _nativeWindow.isClosing;
    internal bool InputBlocked { get; set; }
    internal bool LeftMouseButtonPressed => _nativeWindow.leftMouseButtonPressed;
    internal NativeWindowPointerOperation PointerOperation => _nativeWindow.pointerOperation;
    internal Vector2 ScreenPosition => _nativeWindow.screenPosition;
    internal Rect ScreenBounds => new(_nativeWindow.screenPosition.x, _nativeWindow.screenPosition.y,
        _nativeWindow.windowSize.x, _nativeWindow.windowSize.y);
    internal Fix64 RenderScale => _nativeWindow.renderScale;
    internal IGraphicsDevice? GraphicsDevice => _nativeWindow.graphicsDevice;
    internal IReadOnlyList<EditorWindow> TransientWindows =>
        _transientLayer.Presentations.Select(item => item.Window).ToArray();
    internal Action<GenericMenu, EditorWindow>? PopulateAddNewTabMenu
    {
        get => _populateAddNewTabMenu;
        set
        {
            _populateAddNewTabMenu = value;
            _transientLayer.PopulateAddNewTabMenu = value;
        }
    }
    internal Action<IGraphicsDevice, int, int>? RenderBackground
    {
        set => _nativeWindow.renderBackground = value;
    }

    internal void Initialize()
    {
        _nativeWindow.Initialize();
        _nativeMoveScope ??= Win32NativeMoveScope.TryCreate(_nativeWindow.nativeHandle,
            point => NativeMoveUpdated?.Invoke(this, point),
            point => NativeMoveCompleted?.Invoke(this, point));
        SynchronizeWindowPosition();
    }

    internal void Pump()
    {
        if (_disposed || _nativeWindow.isClosing) return;
        _nativeWindow.Pump();
        EnforceSizeLimits();
        SynchronizeWindowPosition();
    }

    internal void Focus() => _nativeWindow.Focus();

    internal void Move(Vector2 screenPosition)
    {
        _nativeWindow.Move((int)screenPosition.x, (int)screenPosition.y);
        SynchronizeWindowPosition();
    }

    internal void Repaint() => _nativeWindow.Repaint();

    internal void ShowTransient(EditorWindow window, EditorWindowState state)
    {
        _transientLayer.Show(window, state);
        _transientLayer.Focus(window);
        Repaint();
    }

    internal bool ContainsTransient(EditorWindow window) => _transientLayer.Contains(window);

    internal bool FocusTransient(EditorWindow window)
    {
        var focused = _transientLayer.Focus(window);
        if (focused)
        {
            Focus();
            Repaint();
        }
        return focused;
    }

    internal bool RemoveTransient(EditorWindow window)
    {
        var removed = _transientLayer.Remove(window);
        if (removed) Repaint();
        return removed;
    }

    internal Rect ContentRect(int framebufferWidth, int framebufferHeight)
    {
        var scale = Fix64.Max(Fix64.FromDecimal(0.01m), RenderScale);
        var width = Fix64.Max(1, (Fix64)framebufferWidth / scale);
        var height = Fix64.Max(1, (Fix64)framebufferHeight / scale);
        var toolbarHeight = ToolbarHeight();
        return new Rect(0, toolbarHeight, width, Fix64.Max(1, height - toolbarHeight));
    }

    internal void RequestDock(Vector2 mainCanvasPoint) =>
        DockRequested?.Invoke(this, mainCanvasPoint);

    internal void CancelTransientUi()
    {
        _genericMenuPopup.Close();
        _genericMenuAnchor = null;
        _advancedDropdown.Close();
        _objectPicker.Close();
        _transientLayer.DismissPopups();
        _transientLayer.CancelInteractions();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Window.titleContentChanged -= OnTitleContentChanged;
        _nativeWindow.gui -= OnGUI;
        _nativeWindow.closing -= OnNativeClosing;
        _nativeWindow.focusChanged -= OnNativeFocusChanged;
        _transientLayer.WindowClosed -= OnTransientWindowClosed;
        _nativeMoveScope?.Dispose();
        _nativeMoveScope = null;
        _nativeWindow.Dispose();
    }

    private void OnGUI()
    {
        var previousHandler = GenericMenuDispatcher.Handler;
        var previousObjectPickerHandler = EditorObjectPickerPopupDispatcher.Handler;
        GenericMenuDispatcher.Handler = OpenDispatchedMenu;
        EditorObjectPickerPopupDispatcher.Handler = OpenObjectPicker;
        try
        {
            DrawWindowAndTransients();
        }
        finally
        {
            GenericMenuDispatcher.Handler = previousHandler;
            EditorObjectPickerPopupDispatcher.Handler = previousObjectPickerHandler;
        }
    }

    private void DrawWindowAndTransients()
    {
        var inputPass = ImGuiPopupMenu.IsInputEvent(Event.current.type);
        var canvas = new Rect(0, 0, GUIUtility.currentViewWidth, GUIUtility.currentViewHeight);
        if (inputPass && InputBlocked)
        {
            Event.current.Use();
            return;
        }
        if (inputPass)
        {
            DrawPopup();
            if (Event.current.type == EventType.Used) return;
            _transientLayer.Draw(canvas, inputPass: true);
            if (Event.current.type == EventType.Used) return;
        }

        var toolbarHeight = ToolbarHeight();
        var toolbar = new Rect(0, 0, GUIUtility.currentViewWidth, toolbarHeight);
        GUI.PassiveBox(toolbar, GUIContent.none, EditorStyles.toolbar);
        var menuButtonWidth = Fix64.Max(24, EditorStyles.toolbarIconButton.fixedWidth);
        var actionsWidth = menuButtonWidth;
        var labelWidth = Fix64.Max(0, toolbar.width - actionsWidth - 8);
        GUI.Label(new Rect(6, 0, labelWidth, toolbar.height), Window.titleContent,
            EditorStyles.windowTitle);
        var actionX = toolbar.xMax - actionsWidth - 3;
        var menuRect = new Rect(actionX, 2, menuButtonWidth, Fix64.Max(18, toolbar.height - 4));
        if (GUI.Button(menuRect,
                new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.More, string.Empty),
                EditorStyles.toolbarIconButton))
        {
            ShowWindowContextMenu(menuRect);
        }

        var content = new Rect(0, toolbarHeight, GUIUtility.currentViewWidth,
            Fix64.Max(1, GUIUtility.currentViewHeight - toolbarHeight));
        if (IsPointerEvent(Event.current.type))
        {
            if (content.Contains(Event.current.mousePosition)) EditorWindow.SetMouseOverWindow(Window);
            else if (ReferenceEquals(EditorWindow.mouseOverWindow, Window))
                EditorWindow.SetMouseOverWindow(null);
        }

        using (GUI.BeginWindow(content)) Window.OnGUIInternal();

        if (!inputPass)
        {
            _transientLayer.Draw(canvas, inputPass: false);
            DrawPopup();
        }
    }

    private void OpenDispatchedMenu(IReadOnlyList<GenericMenuItem> items)
    {
        _objectPicker.Close();
        var presentation = GenericMenuDispatcher.CurrentPresentation;
        Rect? rootAnchor = null;
        Vector2 position;
        if (presentation.HasAnchor)
        {
            var topLeft = GUI.GUIToRootPoint(presentation.Anchor.position);
            var bottomRight = GUI.GUIToRootPoint(new Vector2(
                presentation.Anchor.xMax, presentation.Anchor.yMax));
            rootAnchor = new Rect(topLeft.x, topLeft.y,
                Fix64.Max(0, bottomRight.x - topLeft.x),
                Fix64.Max(0, bottomRight.y - topLeft.y));
            position = new Vector2(rootAnchor.Value.x, rootAnchor.Value.yMax);
        }
        else
            position = GUI.GUIToRootPoint(Event.current.mousePosition);

        if (presentation.IsAdvanced)
        {
            _genericMenuPopup.Close();
            _genericMenuAnchor = null;
            _advancedDropdown.Open(items, position, rootAnchor);
        }
        else
        {
            _advancedDropdown.Close();
            var screenPosition = _nativeWindow.GUIToScreen(position, GUIUtility.pixelsPerPoint);
            Rect? screenAnchor = null;
            if (rootAnchor is { } anchor)
            {
                var screenTopLeft = _nativeWindow.GUIToScreen(anchor.position, GUIUtility.pixelsPerPoint);
                var screenBottomRight = _nativeWindow.GUIToScreen(
                    new Vector2(anchor.xMax, anchor.yMax), GUIUtility.pixelsPerPoint);
                screenAnchor = new Rect(screenTopLeft.x, screenTopLeft.y,
                    Fix64.Max(0, screenBottomRight.x - screenTopLeft.x),
                    Fix64.Max(0, screenBottomRight.y - screenTopLeft.y));
            }
            if (Win32GenericMenuPresenter.TryShow(
                    _nativeWindow.nativeHandle, screenPosition, items, screenAnchor))
            {
                _genericMenuPopup.Close();
                _genericMenuAnchor = null;
                return;
            }
            _genericMenuAnchor = rootAnchor;
            _genericMenuPopup.Open(items, position);
        }
    }

    private void DrawPopup()
    {
        if (_objectPicker.isOpen) _objectPicker.Draw();
        else if (_advancedDropdown.isOpen) _advancedDropdown.Draw();
        else if (!_genericMenuPopup.Draw(_genericMenuAnchor)) _genericMenuAnchor = null;
    }

    private void OpenObjectPicker(EditorObjectPickerRequest request)
    {
        var topLeft = GUI.GUIToRootPoint(request.Anchor.position);
        var bottomRight = GUI.GUIToRootPoint(new Vector2(request.Anchor.xMax, request.Anchor.yMax));
        var rootAnchor = new Rect(topLeft.x, topLeft.y,
            Fix64.Max(0, bottomRight.x - topLeft.x), Fix64.Max(0, bottomRight.y - topLeft.y));
        _genericMenuPopup.Close();
        _genericMenuAnchor = null;
        _advancedDropdown.Close();
        _objectPicker.Open(request, new Vector2(rootAnchor.x, rootAnchor.yMax), rootAnchor);
    }

    private void ShowWindowContextMenu(Rect anchor)
    {
        var menu = new GenericMenu();
        Window.PopulateContextMenu(menu);
        if (menu.GetItemCount() > 0) menu.AddSeparator(string.Empty);
        PopulateAddNewTabMenu?.Invoke(menu, Window);
        if (PopulateAddNewTabMenu is not null) menu.AddSeparator(string.Empty);
        if (State == EditorWindowState.Normal)
            menu.AddItem(new GUIContent("Dock"), false, () =>
            {
                var point = ImGuiNativeWindow.TryGetPointerScreenPosition(out var screenPoint)
                    ? screenPoint
                    : ScreenPosition;
                DockRequested?.Invoke(this, point);
            });
        menu.AddItem(new GUIContent(_nativeWindow.isMaximized ? "Minimize" : "Maximize"),
            _nativeWindow.isMaximized, () => _nativeWindow.SetMaximized(!_nativeWindow.isMaximized));
        menu.AddItem(new GUIContent("Minimize to Taskbar"), false, _nativeWindow.Minimize);
        if (Window.supportsLocking)
            menu.AddItem(new GUIContent(Window.isLocked ? "Unlock" : "Lock"), Window.isLocked,
                () => Window.isLocked = !Window.isLocked);
        menu.AddItem(new GUIContent("Close Window"), false, Window.Close);
        menu.DropDown(anchor);
    }

    private void SynchronizeWindowPosition() => Window.position = ScreenBounds;

    private void EnforceSizeLimits()
    {
        var (minimumSize, maximumSize) = ClientSizeLimits();
        _nativeWindow.SetSizeLimits(
            CeilingToInt(minimumSize.x), CeilingToInt(minimumSize.y),
            FloorToInt(maximumSize.x), FloorToInt(maximumSize.y));
    }

    private (Vector2 Minimum, Vector2 Maximum) ClientSizeLimits()
    {
        var minimum = ImGuiNativeWindow.GUIToClient(Window.minSize, GUIUtility.pixelsPerPoint);
        var maximum = ImGuiNativeWindow.GUIToClient(Window.maxSize, GUIUtility.pixelsPerPoint);
        return (new Vector2(Fix64.Max(1, minimum.x), Fix64.Max(1, minimum.y)),
            new Vector2(Fix64.Max(minimum.x, maximum.x), Fix64.Max(minimum.y, maximum.y)));
    }

    private static int CeilingToInt(Fix64 value) =>
        (int)Math.Clamp(Math.Ceiling((double)value), 1, int.MaxValue);

    private static int FloorToInt(Fix64 value) =>
        (int)Math.Clamp(Math.Floor((double)value), 1, int.MaxValue);

    private void OnNativeClosing() => CloseRequested?.Invoke(this);

    private void OnNativeFocusChanged(bool focused)
    {
        if (!focused) CancelTransientUi();
        FocusChanged?.Invoke(this, focused);
    }

    private void OnTransientWindowClosed(EditorWindow window) =>
        TransientClosed?.Invoke(this, window);

    private void OnTitleContentChanged(EditorWindow window) =>
        _nativeWindow.SetTitle(WindowTitle(window));

    private static string WindowTitle(EditorWindow window) =>
        string.IsNullOrWhiteSpace(window.titleContent.text)
            ? "BEngine"
            : $"BEngine - {window.titleContent.text}";

    private static Fix64 ToolbarHeight() => Fix64.Max(24,
        Fix64.Max(EditorStyles.toolbar.fixedHeight, EditorStyles.toolbarButton.fixedHeight + 4));

    private static bool IsPointerEvent(EventType type) => type is
        EventType.MouseDown or EventType.MouseUp or EventType.MouseMove or EventType.MouseDrag or
        EventType.ContextClick or EventType.ScrollWheel or EventType.MouseEnterWindow or
        EventType.MouseLeaveWindow;
}
