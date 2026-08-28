using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.Serialization;
using BEngine.Documents;
using BEngine.Editor.Documents;
using NVector4 = System.Numerics.Vector4;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.Editor;

internal sealed class ImGuiDockWorkspace
{
    private readonly List<ImGuiDockPanel> _panels = [];
    private readonly Dictionary<DockArea, DockGroup> _defaults;
    private DockNode? _root;
    private ImGuiDockPanel? _pressedTab;
    private ImGuiDockPanel? _dragging;
    private Vector2 _pressPosition;
    private DockGroup? _dropTarget;
    private DockGroup? _maximizedGroup;
    private DockDropPosition _dropPosition;
    private EditorWindow? _hoveredWindow;
    private Vector2? _externalDragPoint;
    private Rect _workspaceBounds;
    public bool HostIsInteractive { get; set; } = true;
    public IReadOnlyList<ImGuiDockPanel> Panels => _panels;
    public event Action<ImGuiDockPanel, Vector2>? UndockRequested;

    public ImGuiDockWorkspace()
    {
        var left = new DockGroup();
        var center = new DockGroup();
        var right = new DockGroup();
        var bottom = new DockGroup();
        _defaults = new Dictionary<DockArea, DockGroup>
        {
            [DockArea.Left] = left,
            [DockArea.Center] = center,
            [DockArea.Right] = right,
            [DockArea.Bottom] = bottom
        };
        var centerRight = new DockSplit(true, Fix64.FromDecimal(0.72m), center, right);
        var top = new DockSplit(true, Fix64.FromDecimal(0.19m), left, centerRight);
        _root = new DockSplit(false, Fix64.FromDecimal(0.72m), top, bottom);
    }

    public ImGuiDockPanel Add(string id, EditorWindow window, DockArea area, bool select)
    {
        var group = _defaults[area];
        EnsureAttached(group, area);
        var panel = new ImGuiDockPanel(id, window, area) { Group = group };
        group.Panels.Add(panel);
        _panels.Add(panel);
        if (select || group.SelectedId is null) group.SelectedId = id;
        return panel;
    }

    public void Show(string id)
    {
        var panel = _panels.FirstOrDefault(item => item.Id == id); if (panel is null) return;
        panel.Visible = true;
        var group = panel.Group ?? _defaults[DockArea.Center];
        EnsureAttached(group, DockArea.Center);
        if (!group.Panels.Contains(panel)) group.Panels.Add(panel);
        panel.Group = group;
        group.SelectedId = panel.Id;
        panel.Window.FocusInternal();
    }

    public void Remove(string id)
    {
        var panel = _panels.FirstOrDefault(item => item.Id == id);
        if (panel is null) return;
        _panels.Remove(panel);
        if (panel.Group is not { } group) return;
        group.Panels.Remove(panel);
        panel.Group = null;
        if (group.SelectedId == id) group.SelectedId = group.Panels.FirstOrDefault()?.Id;
        if (group.Panels.Count == 0) Collapse(group);
    }

    public ImGuiDockPanel DockExternal(string id, EditorWindow window, Vector2 point)
    {
        var panel = new ImGuiDockPanel(id, window);
        _panels.Add(panel);
        if (!TryGetDrop(point, out var target, out var position, out _))
        {
            target = _defaults[DockArea.Center];
            EnsureAttached(target, DockArea.Center);
            position = DockDropPosition.Center;
        }
        AttachPanel(panel, target, position);
        panel.Visible = true;
        window.FocusInternal();
        return panel;
    }

    public void SetExternalDragPoint(Vector2? point) => _externalDragPoint = point;

    internal void CancelInteractions()
    {
        _pressedTab = null;
        _dragging = null;
        _dropTarget = null;
        _externalDragPoint = null;
        _hoveredWindow = null;
    }

    public EditorDockNodeDocument? CaptureLayout() => CaptureNode(_root);

    public IReadOnlyDictionary<EditorWindow, ImGuiDockPanel> RestoreLayout(
        EditorDockNodeDocument? rootDocument,
        IReadOnlyDictionary<string, EditorWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        var preferredAreas = _panels.ToDictionary(panel => panel.Id, panel => panel.PreferredArea,
            StringComparer.Ordinal);
        _panels.Clear();
        _pressedTab = null;
        _dragging = null;
        _dropTarget = null;
        _maximizedGroup = null;
        foreach (var group in _defaults.Values.Distinct())
        {
            group.Panels.Clear();
            group.SelectedId = null;
            group.Parent = null;
        }

        var included = new HashSet<string>(StringComparer.Ordinal);
        _root = BuildNode(rootDocument, windows, preferredAreas, included);
        foreach (var area in Enum.GetValues<DockArea>())
        {
            var matching = _panels.FirstOrDefault(panel => panel.PreferredArea == area)?.Group;
            _defaults[area] = matching ?? new DockGroup();
        }
        foreach (var (id, window) in windows)
        {
            if (included.Contains(id)) continue;
            Add(id, window, preferredAreas.GetValueOrDefault(id, DockArea.Center), true);
        }
        return _panels.ToDictionary(panel => panel.Window, panel => panel);
    }

    public string? MaximizedPanelId => _maximizedGroup?.SelectedId;

    public bool IsMaximized(EditorWindow window) =>
        _panels.FirstOrDefault(panel => ReferenceEquals(panel.Window, window)) is { Group: { } group } &&
        ReferenceEquals(_maximizedGroup, group);

    public bool ToggleMaximize(EditorWindow window)
    {
        var panel = _panels.FirstOrDefault(item => ReferenceEquals(item.Window, window));
        if (panel is not { Visible: true, Group: { } group }) return false;
        group.SelectedId = panel.Id;
        _maximizedGroup = ReferenceEquals(_maximizedGroup, group) ? null : group;
        panel.Window.FocusInternal();
        return true;
    }

    public void RestoreMaximizedPanel(string? panelId)
    {
        _maximizedGroup = string.IsNullOrWhiteSpace(panelId)
            ? null
            : _panels.FirstOrDefault(panel => panel.Id == panelId)?.Group;
    }

    public bool IsSelected(EditorWindow window) =>
        _panels.FirstOrDefault(panel => ReferenceEquals(panel.Window, window)) is { Group: { } group } panel &&
        panel.Visible && group.SelectedId == panel.Id;

    public void OnGUI(Rect rect)
    {
        _workspaceBounds = rect;
        _hoveredWindow = null;
        if (_root is null) GUI.Box(rect, string.Empty);
        else if (_maximizedGroup is not null) DrawNode(_maximizedGroup, rect);
        else DrawNode(_root, rect);
        DrawDockHint();
        DrawExternalDockHint();
        if (HostIsInteractive)
            EditorWindow.SetMouseOverWindow(_hoveredWindow);
        else if (_panels.Any(panel => ReferenceEquals(panel.Window, EditorWindow.mouseOverWindow)))
            EditorWindow.SetMouseOverWindow(null);
    }

    private void DrawNode(DockNode node, Rect rect)
    {
        node.Bounds = rect;
        if (node is DockGroup group) DrawGroup(group, rect);
        else DrawSplit((DockSplit)node, rect);
    }

    private void DrawSplit(DockSplit split, Rect rect)
    {
        const int splitterVisualSize = 1;
        const int splitterHitSize = 5;
        var available = Fix64.Max(0, (split.SideBySide ? rect.width : rect.height) - splitterVisualSize);
        var minimum = Fix64.Min(120, available / 2);
        var firstSize = Fix64.Clamp(available * split.Ratio, minimum, Fix64.Max(minimum, available - minimum));
        Rect firstRect, separator, separatorHitRect, secondRect;
        if (split.SideBySide)
        {
            firstRect = new Rect(rect.x, rect.y, firstSize, rect.height);
            separator = new Rect(firstRect.xMax, rect.y, splitterVisualSize, rect.height);
            separatorHitRect = new Rect(separator.x - (splitterHitSize - splitterVisualSize) / 2,
                rect.y, splitterHitSize, rect.height);
            secondRect = new Rect(separator.xMax, rect.y, Fix64.Max(0, rect.xMax - separator.xMax), rect.height);
        }
        else
        {
            firstRect = new Rect(rect.x, rect.y, rect.width, firstSize);
            separator = new Rect(rect.x, firstRect.yMax, rect.width, splitterVisualSize);
            separatorHitRect = new Rect(rect.x,
                separator.y - (splitterHitSize - splitterVisualSize) / 2, rect.width, splitterHitSize);
            secondRect = new Rect(rect.x, separator.yMax, rect.width, Fix64.Max(0, rect.yMax - separator.yMax));
        }

        var evt = Event.current;
        var pointer = evt.mousePosition;
        if (evt.type == EventType.MouseDown && evt.button == 0 && separatorHitRect.Contains(pointer))
        {
            GUIUtility.hotControl = split.ControlId;
            evt.Use();
        }
        else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == split.ControlId)
        {
            var relative = split.SideBySide ? pointer.x - rect.x : pointer.y - rect.y;
            split.Ratio = Fix64.Clamp(relative / Fix64.Max(1, available), Fix64.FromDecimal(0.08m),
                Fix64.FromDecimal(0.92m));
            evt.Use();
        }
        else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == split.ControlId)
        {
            GUIUtility.hotControl = 0;
            evt.Use();
        }
        if (Event.current.type == EventType.Repaint)
            GUI.Box(separator, GUIContent.none, EditorStyles.separator);
        DrawNode(split.First, firstRect);
        DrawNode(split.Second, secondRect);
        EditorGUIUtility.AddCursorRect(separatorHitRect, split.SideBySide
            ? MouseCursor.SplitResizeLeftRight
            : MouseCursor.SplitResizeUpDown);
    }

    private void DrawGroup(DockGroup group, Rect rect)
    {
        var panels = group.Panels.Where(item => item.Visible).ToArray();
        if (panels.Length == 0) return;
        var selected = panels.FirstOrDefault(item => item.Id == group.SelectedId) ?? panels[0];
        group.SelectedId = selected.Id;
        var sceneSurface = selected.Window.titleContent.text is "Scene" or "Game";
        if (!sceneSurface) GUI.Box(rect, GUIContent.none, GUI.skin.window);
        var titleBarHeight = Fix64.Max(EditorStyles.windowTitle.fixedHeight + 2,
            Fix64.Max(EditorStyles.dockTab.fixedHeight + 2, EditorStyles.toolbarIconButton.fixedHeight + 2));
        var titleBar = new Rect(rect.x, rect.y, rect.width, titleBarHeight);
        GUI.Box(titleBar, GUIContent.none, EditorStyles.windowTitle);

        var actionHeight = Fix64.Min(titleBarHeight - 2,
            Fix64.Max(18, EditorStyles.toolbarIconButton.fixedHeight));
        var actionY = rect.y + (titleBarHeight - actionHeight) / 2;
        var actionWidth = Fix64.Max(24, EditorStyles.toolbarIconButton.fixedWidth);
        var showLock = selected.Window.supportsLocking;
        var actionCount = showLock ? 4 : 3;
        var actionsWidth = actionWidth * actionCount + (actionCount - 1) * 2;
        var tabAreaWidth = Fix64.Max(40, rect.width - actionsWidth - 4);
        var tabHeight = Fix64.Min(titleBarHeight - 2, Fix64.Max(18, EditorStyles.dockTab.fixedHeight));
        var tabY = rect.y + titleBarHeight - tabHeight;
        var tabWidths = panels.Select(panel => Fix64.Max(64,
            EditorStyles.dockTab.CalcSize(panel.Window.titleContent).x +
            (string.IsNullOrWhiteSpace(panel.Window.titleContent.image) ? 14 : 34))).ToArray();
        var totalWidth = tabWidths.Aggregate(Fix64.Zero, (sum, width) => sum + width);
        var tabLayoutSignature = TabLayoutSignature(panels, tabWidths);
        var tabViewportWidth = tabAreaWidth;
        if (panels.Length > 1 && totalWidth > tabViewportWidth)
        {
            var scrollButtonWidth = Fix64.Max(20, Fix64.Min(actionWidth, 24));
            tabViewportWidth = Fix64.Max(40, tabAreaWidth - scrollButtonWidth * 2 - 2);
            if (!string.Equals(group.TabVisibilitySelectedId, selected.Id, StringComparison.Ordinal) ||
                group.TabVisibilityViewportWidth != tabViewportWidth ||
                group.TabVisibilityTotalWidth != totalWidth ||
                group.TabVisibilityLayoutSignature != tabLayoutSignature)
            {
                KeepSelectedTabVisible(group, panels, tabWidths, tabViewportWidth);
                group.TabVisibilitySelectedId = selected.Id;
                group.TabVisibilityViewportWidth = tabViewportWidth;
                group.TabVisibilityTotalWidth = totalWidth;
                group.TabVisibilityLayoutSignature = tabLayoutSignature;
            }
            if (EditorToolbar.Button(new Rect(rect.x + tabViewportWidth + 2, actionY,
                    scrollButtonWidth, actionHeight),
                    new GUIContent("<", tooltip: "Previous tabs"))) group.TabOffset -= 90;
            if (EditorToolbar.Button(new Rect(rect.x + tabViewportWidth + scrollButtonWidth + 3, actionY,
                    scrollButtonWidth, actionHeight),
                    new GUIContent(">", tooltip: "Next tabs"))) group.TabOffset += 90;
            group.TabOffset = Fix64.Clamp(group.TabOffset, 0, Fix64.Max(0, totalWidth - tabViewportWidth));
        }
        else
        {
            group.TabOffset = 0;
            group.TabVisibilitySelectedId = selected.Id;
            group.TabVisibilityViewportWidth = tabViewportWidth;
            group.TabVisibilityTotalWidth = totalWidth;
            group.TabVisibilityLayoutSignature = tabLayoutSignature;
        }

        var tabViewport = new Rect(rect.x + 2, tabY, Fix64.Max(1, tabViewportWidth - 2), tabHeight);
        GUI.BeginClip(tabViewport);
        var tabX = rect.x + 2 - group.TabOffset;
        var pointer = Event.current.mousePosition;
        for (var index = 0; index < panels.Length; index++)
        {
            var panel = panels[index];
            var width = panels.Length == 1
                ? Fix64.Min(tabWidths[index], tabViewport.width)
                : tabWidths[index];
            var tab = new Rect(tabX, tabY, width, tabHeight);
            var visibleTab = tab.xMax >= tabViewport.x && tab.x <= tabViewport.xMax;
            var active = panel.Id == group.SelectedId;
            var style = active ? EditorStyles.dockTabActive : EditorStyles.dockTab;
            var tabContent = FitTabContent(panel.Window.titleContent, GUI.GetVisibleWidth(tab), style);
            var clicked = visibleTab && GUI.Button(tab, tabContent, style);
            if (visibleTab && active && Event.current.type == EventType.Repaint)
                GUI.DrawRect(new Rect(tab.x, tab.y, tab.width, 2),
                    panel.Window.hasFocus
                        ? EditorStyles.progressBarBar.normal.backgroundColor
                        : EditorStyles.dockTab.normal.textColor);
            if (visibleTab && Event.current.type == EventType.ContextClick && tab.Contains(pointer))
            {
                group.SelectedId = panel.Id;
                panel.Window.FocusInternal();
                ShowWindowContextMenu(panel, pointer);
                Event.current.Use();
            }
            if (Event.current.rawType == EventType.MouseDown && Event.current.button == 0 &&
                visibleTab && tab.Contains(pointer))
            {
                _pressedTab = panel;
                _pressPosition = pointer;
                group.SelectedId = panel.Id;
                panel.Window.FocusInternal();
            }
            if (Event.current.rawType == EventType.MouseDrag && _pressedTab is not null)
            {
                var distance = Vector2.Distance(pointer, _pressPosition);
                if (distance > 4) _dragging = _pressedTab;
            }
            if (clicked && _dragging is null && group.SelectedId != panel.Id)
            {
                group.SelectedId = panel.Id;
                panel.Window.FocusInternal();
            }
            tabX += width;
        }
        GUI.EndClip();

        var actionX = rect.xMax - actionsWidth;
        if (showLock)
        {
            var lockRect = new Rect(actionX, actionY, actionWidth, actionHeight);
            if (EditorToolbar.Button(lockRect, new GUIContent(string.Empty,
                    selected.Window.isLocked ? EditorBuiltinIcons.Toolbar.Lock :
                        EditorBuiltinIcons.Toolbar.Unlock, string.Empty)))
                selected.Window.isLocked = !selected.Window.isLocked;
            actionX = lockRect.xMax + 2;
        }
        var menuRect = new Rect(actionX, actionY, actionWidth, actionHeight);
        if (EditorToolbar.Button(menuRect,
                new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.More, string.Empty)))
            ShowWindowContextMenu(selected, new Vector2(menuRect.x, menuRect.yMax));
        var maximizeRect = new Rect(menuRect.xMax + 2, actionY, actionWidth, actionHeight);
        if (EditorToolbar.Button(maximizeRect, new GUIContent(
                ReferenceEquals(_maximizedGroup, group) ? "-" : "[]")))
            _maximizedGroup = ReferenceEquals(_maximizedGroup, group) ? null : group;
        var closeRect = new Rect(maximizeRect.xMax + 2, actionY, actionWidth, actionHeight);
        if (EditorToolbar.Button(closeRect, new GUIContent("x")))
        {
            selected.Window.Close();
            return;
        }

        var content = new Rect(rect.x + 2, rect.y + titleBarHeight, Fix64.Max(1, rect.width - 4),
            Fix64.Max(1, rect.height - titleBarHeight - 2));
        selected.Window.position = content;
        if (HostIsInteractive && content.Contains(pointer)) _hoveredWindow = selected.Window;
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && content.Contains(pointer))
            selected.Window.FocusInternal();
        if (ShouldDispatch(selected.Window, content, pointer))
            using (GUI.BeginWindow(content)) selected.Window.OnGUIInternal();
        DrawBorder(rect, 2);
    }

    private static void KeepSelectedTabVisible(DockGroup group, IReadOnlyList<ImGuiDockPanel> panels,
        IReadOnlyList<Fix64> widths, Fix64 viewportWidth)
    {
        var selectedIndex = -1;
        for (var index = 0; index < panels.Count; index++)
        {
            if (panels[index].Id != group.SelectedId) continue;
            selectedIndex = index;
            break;
        }
        if (selectedIndex < 0) return;
        var start = Fix64.Zero;
        for (var index = 0; index < selectedIndex; index++) start += widths[index];
        var end = start + widths[selectedIndex];
        if (widths[selectedIndex] >= viewportWidth)
        {
            group.TabOffset = start;
            return;
        }
        if (start < group.TabOffset) group.TabOffset = start;
        else if (end > group.TabOffset + viewportWidth) group.TabOffset = end - viewportWidth;
    }

    private static GUIContent FitTabContent(GUIContent source, Fix64 visibleWidth, GUIStyle style)
    {
        var hasImage = !string.IsNullOrWhiteSpace(source.image);
        var availableTextWidth = Fix64.Max(0, visibleWidth - (hasImage ? 24 : 8));
        var fittedText = FitText(source.text, availableTextWidth, style.fontSize);
        if (fittedText.Equals(source.text, StringComparison.Ordinal)) return source;
        var tooltip = string.IsNullOrWhiteSpace(source.tooltip) ? source.text : source.tooltip;
        return new GUIContent(fittedText, source.image, tooltip);
    }

    private static int TabLayoutSignature(IReadOnlyList<ImGuiDockPanel> panels,
        IReadOnlyList<Fix64> widths)
    {
        var hash = new HashCode();
        for (var index = 0; index < panels.Count; index++)
        {
            hash.Add(panels[index].Id, StringComparer.Ordinal);
            hash.Add(widths[index]);
        }
        return hash.ToHashCode();
    }

    private static string FitText(string text, Fix64 availableWidth, Fix64 fontSize)
    {
        if (string.IsNullOrEmpty(text) || availableWidth <= 0) return string.Empty;
        if (GUITextMetrics.MeasureWidth(text, fontSize, GUIUtility.fontFamily) <= availableWidth) return text;

        var suffix = "...";
        while (suffix.Length > 0 &&
               GUITextMetrics.MeasureWidth(suffix, fontSize, GUIUtility.fontFamily) > availableWidth)
            suffix = suffix[..^1];
        if (suffix.Length == 0) return string.Empty;

        var elementStarts = StringInfo.ParseCombiningCharacters(text);
        var low = 0;
        var high = elementStarts.Length;
        while (low < high)
        {
            var count = (low + high + 1) / 2;
            var end = count == elementStarts.Length ? text.Length : elementStarts[count];
            var candidate = string.Concat(text.AsSpan(0, end), suffix);
            if (GUITextMetrics.MeasureWidth(candidate, fontSize, GUIUtility.fontFamily) <= availableWidth)
                low = count;
            else
                high = count - 1;
        }
        var prefixEnd = low == elementStarts.Length ? text.Length : elementStarts[low];
        return string.Concat(text.AsSpan(0, prefixEnd), suffix);
    }

    private void ShowWindowContextMenu(ImGuiDockPanel panel, Vector2 undockPosition)
    {
        var menu = new GenericMenu();
        panel.Window.PopulateContextMenu(menu);
        if (menu.GetItemCount() > 0) menu.AddSeparator(string.Empty);
        menu.AddItem(new GUIContent("Float"), false,
            () => EditorCallbackDispatcher.Invoke(UndockRequested, panel, undockPosition,
                nameof(UndockRequested)));
        menu.AddItem(new GUIContent(panel.Window.isLocked ? "Unlock" : "Lock"),
            panel.Window.isLocked, () => panel.Window.isLocked = !panel.Window.isLocked);
        menu.AddItem(new GUIContent("Close Tab"), false, panel.Window.Close);
        menu.ShowAsContext();
    }

    private static bool ShouldDispatch(EditorWindow window, Rect content, Vector2 pointer)
    {
        var type = Event.current.type;
        if (type is EventType.Layout or EventType.Repaint) return true;
        if (type is EventType.KeyDown or EventType.KeyUp or EventType.ValidateCommand or EventType.ExecuteCommand)
            return window.hasFocus;
        if (type is EventType.MouseDrag or EventType.MouseUp && GUIUtility.hotControl != 0)
            return window.hasFocus;
        if (type == EventType.MouseMove && !window.wantsMouseMove &&
            (window.eventInterests & EventInterests.WantsMouseMove) == 0) return false;
        return content.Contains(pointer);
    }

    private void DrawDockHint()
    {
        if (_dragging is null)
        {
            if (Event.current.rawType == EventType.MouseUp) _pressedTab = null;
            return;
        }
        var point = Event.current.mousePosition;
        if (!TryGetDrop(point, out var target, out var position, out var preview))
        {
            var outsideWorkspace = !_workspaceBounds.Contains(point);
            if (outsideWorkspace && Event.current.rawType is EventType.MouseDrag or EventType.MouseUp)
            {
                var panel = _dragging;
                ClearTabDrag();
                if (Event.current.type != EventType.Used) Event.current.Use();
                EditorCallbackDispatcher.Invoke(UndockRequested, panel, point, nameof(UndockRequested));
            }
            else if (Event.current.rawType == EventType.MouseUp)
            {
                ClearTabDrag();
                if (Event.current.type != EventType.Used) Event.current.Use();
            }
            return;
        }
        _dropTarget = target;
        _dropPosition = position;
        DrawDockPreview(preview);
        if (Event.current.rawType == EventType.MouseUp)
        {
            PerformDrop(_dragging, _dropTarget, _dropPosition);
            ClearTabDrag();
            if (Event.current.type != EventType.Used) Event.current.Use();
        }
    }

    private void ClearTabDrag()
    {
        _dragging = null;
        _pressedTab = null;
        _dropTarget = null;
        GUIUtility.hotControl = 0;
    }

    private void DrawExternalDockHint()
    {
        if (_dragging is not null || _externalDragPoint is not { } point ||
            !TryGetDrop(point, out _, out _, out var preview)) return;
        DrawDockPreview(preview);
    }

    private static void DrawDockPreview(Rect preview)
    {
        var accent = EditorStyles.selectionRect.normal.backgroundColor;
        GUI.DrawRect(preview, new Color(accent.r, accent.g, accent.b, Fix64.FromDecimal(0.22m)));
        DrawBorder(preview, 2, accent);
    }

    private void PerformDrop(ImGuiDockPanel panel, DockGroup target, DockDropPosition position)
    {
        var source = panel.Group;
        if (source is null) return;
        if (ReferenceEquals(source, target) && position == DockDropPosition.Center)
        {
            target.SelectedId = panel.Id;
            panel.Window.FocusInternal();
            return;
        }
        if (ReferenceEquals(source, target) && source.Panels.Count == 1) return;

        source.Panels.Remove(panel);
        if (source.SelectedId == panel.Id) source.SelectedId = source.Panels.FirstOrDefault()?.Id;
        AttachPanel(panel, target, position);
        if (source.Panels.Count == 0) Collapse(source);
        panel.Window.FocusInternal();
    }

    private void AttachPanel(ImGuiDockPanel panel, DockGroup target, DockDropPosition position)
    {
        if (position == DockDropPosition.Center)
        {
            target.Panels.Add(panel);
            panel.Group = target;
            target.SelectedId = panel.Id;
            return;
        }

        var newGroup = new DockGroup();
        newGroup.Panels.Add(panel);
        newGroup.SelectedId = panel.Id;
        panel.Group = newGroup;
        var parent = target.Parent;
        var first = position is DockDropPosition.Left or DockDropPosition.Top ? (DockNode)newGroup : target;
        var second = ReferenceEquals(first, newGroup) ? (DockNode)target : newGroup;
        var split = new DockSplit(position is DockDropPosition.Left or DockDropPosition.Right,
            Fix64.Half, first, second) { Parent = parent };
        ReplaceChild(parent, target, split);
    }

    private bool TryGetDrop(Vector2 point, out DockGroup target, out DockDropPosition position,
        out Rect preview)
    {
        var node = _maximizedGroup ?? _root;
        target = FindGroup(node, point)!;
        if (target is null)
        {
            position = default;
            preview = default;
            return false;
        }

        var bounds = target.Bounds;
        var edgeX = Fix64.Min(90, bounds.width * Fix64.FromDecimal(0.25m));
        var edgeY = Fix64.Min(70, bounds.height * Fix64.FromDecimal(0.25m));
        position = point.x < bounds.x + edgeX ? DockDropPosition.Left :
            point.x > bounds.xMax - edgeX ? DockDropPosition.Right :
            point.y < bounds.y + edgeY ? DockDropPosition.Top :
            point.y > bounds.yMax - edgeY ? DockDropPosition.Bottom : DockDropPosition.Center;
        preview = position switch
        {
            DockDropPosition.Left => new Rect(bounds.x, bounds.y, bounds.width / 2, bounds.height),
            DockDropPosition.Right => new Rect(bounds.x + bounds.width / 2, bounds.y,
                bounds.width / 2, bounds.height),
            DockDropPosition.Top => new Rect(bounds.x, bounds.y, bounds.width, bounds.height / 2),
            DockDropPosition.Bottom => new Rect(bounds.x, bounds.y + bounds.height / 2,
                bounds.width, bounds.height / 2),
            _ => bounds
        };
        return true;
    }

    internal bool CanDockAt(Vector2 point) => _workspaceBounds.Contains(point) &&
                                               TryGetDrop(point, out _, out _, out _);

    private static EditorDockNodeDocument? CaptureNode(DockNode? node)
    {
        return node switch
        {
            null => null,
            DockGroup group => new EditorDockNodeDocument
            {
                Type = "Group",
                Panels = group.Panels.Where(panel => panel.Visible).Select(panel => panel.Id).ToList(),
                SelectedId = group.SelectedId
            },
            DockSplit split => new EditorDockNodeDocument
            {
                Type = "Split",
                SideBySide = split.SideBySide,
                Ratio = (float)split.Ratio,
                First = CaptureNode(split.First),
                Second = CaptureNode(split.Second)
            },
            _ => throw new InvalidOperationException($"Unsupported dock node {node.GetType().Name}.")
        };
    }

    private DockNode? BuildNode(EditorDockNodeDocument? document,
        IReadOnlyDictionary<string, EditorWindow> windows,
        IReadOnlyDictionary<string, DockArea> preferredAreas,
        ISet<string> included)
    {
        if (document is null) return null;
        if (document.Type.Equals("Group", StringComparison.OrdinalIgnoreCase))
        {
            var group = new DockGroup();
            foreach (var id in document.Panels)
            {
                if (!included.Add(id) || !windows.TryGetValue(id, out var window)) continue;
                var panel = new ImGuiDockPanel(id, window,
                    preferredAreas.GetValueOrDefault(id, DockArea.Center)) { Group = group };
                group.Panels.Add(panel);
                _panels.Add(panel);
            }
            group.SelectedId = group.Panels.Any(panel => panel.Id == document.SelectedId)
                ? document.SelectedId
                : group.Panels.FirstOrDefault()?.Id;
            return group.Panels.Count > 0 ? group : null;
        }

        var first = BuildNode(document.First, windows, preferredAreas, included);
        var second = BuildNode(document.Second, windows, preferredAreas, included);
        if (first is null) return second;
        if (second is null) return first;
        return new DockSplit(document.SideBySide, Fix64.Clamp((Fix64)document.Ratio,
            Fix64.FromDecimal(0.08m), Fix64.FromDecimal(0.92m)), first, second);
    }

    private void EnsureAttached(DockGroup group, DockArea preferredArea)
    {
        if (ReferenceEquals(_root, group) || group.Parent is not null) return;
        if (_root is null) { _root = group; return; }
        var sideBySide = preferredArea is DockArea.Left or DockArea.Right;
        var first = preferredArea == DockArea.Left ? (DockNode)group : _root;
        var second = ReferenceEquals(first, group) ? _root : group;
        var ratio = preferredArea switch
        {
            DockArea.Left => Fix64.FromDecimal(0.25m),
            DockArea.Bottom => Fix64.FromDecimal(0.72m),
            _ => Fix64.FromDecimal(0.75m)
        };
        _root = new DockSplit(sideBySide, ratio, first, second);
    }

    private void Collapse(DockGroup group)
    {
        if (ReferenceEquals(_maximizedGroup, group)) _maximizedGroup = null;
        var parent = group.Parent;
        if (parent is null)
        {
            if (ReferenceEquals(_root, group)) _root = null;
            return;
        }
        var sibling = ReferenceEquals(parent.First, group) ? parent.Second : parent.First;
        var grandParent = parent.Parent;
        sibling.Parent = grandParent;
        ReplaceChild(grandParent, parent, sibling);
        group.Parent = null;
    }

    private void ReplaceChild(DockSplit? parent, DockNode oldNode, DockNode newNode)
    {
        if (parent is null)
        {
            _root = newNode;
            newNode.Parent = null;
            return;
        }
        if (ReferenceEquals(parent.First, oldNode)) parent.First = newNode;
        else if (ReferenceEquals(parent.Second, oldNode)) parent.Second = newNode;
        newNode.Parent = parent;
    }

    private static DockGroup? FindGroup(DockNode? node, Vector2 point)
    {
        if (node is null || !node.Bounds.Contains(point)) return null;
        return node is DockGroup group ? group :
            FindGroup(((DockSplit)node).First, point) ?? FindGroup(((DockSplit)node).Second, point);
    }

    private static void DrawBorder(Rect rect, Fix64 width)
        => DrawBorder(rect, width, EditorStyles.separator.normal.backgroundColor);

    private static void DrawBorder(Rect rect, Fix64 width, Color color)
    {
        if (Event.current.type != EventType.Repaint) return;
        GUI.DrawRect(new Rect(rect.x, rect.y, rect.width, width), color);
        GUI.DrawRect(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
        GUI.DrawRect(new Rect(rect.x, rect.y, width, rect.height), color);
        GUI.DrawRect(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
    }

    internal abstract class DockNode
    {
        public DockSplit? Parent { get; set; }
        public Rect Bounds { get; set; }
    }

    internal sealed class DockGroup : DockNode
    {
        public List<ImGuiDockPanel> Panels { get; } = [];
        public string? SelectedId { get; set; }
        public Fix64 TabOffset { get; set; }
        public string? TabVisibilitySelectedId { get; set; }
        public Fix64 TabVisibilityViewportWidth { get; set; } = -1;
        public Fix64 TabVisibilityTotalWidth { get; set; } = -1;
        public int TabVisibilityLayoutSignature { get; set; }
    }

    internal sealed class DockSplit : DockNode
    {
        private static int _nextControlId = 73000;
        public DockSplit(bool sideBySide, Fix64 ratio, DockNode first, DockNode second)
        {
            SideBySide = sideBySide;
            Ratio = ratio;
            First = first;
            Second = second;
            ControlId = Interlocked.Increment(ref _nextControlId);
        }
        public bool SideBySide { get; }
        public Fix64 Ratio { get; set; }
        public int ControlId { get; }
        public DockNode First { get; set { field = value; value.Parent = this; } }
        public DockNode Second { get; set { field = value; value.Parent = this; } }
    }

    private enum DockDropPosition { Left, Right, Top, Bottom, Center }
}
