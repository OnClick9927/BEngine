namespace BEngine.Editor;

internal sealed class ImGuiPopupMenu
{
    private readonly List<PopupNode> _roots = [];
    private readonly List<PopupNode> _openPath = [];
    private readonly List<Rect> _visibleRects = [];
    private Vector2 _position;

    public bool isOpen => _roots.Count > 0;

    public void Open(IEnumerable<GenericMenuItem> items, Vector2 position)
    {
        ArgumentNullException.ThrowIfNull(items);
        _roots.Clear();
        _openPath.Clear();
        _position = position;
        foreach (var item in items) Add(item);
        if (_roots.Count == 0) Close();
    }

    public void Close()
    {
        _roots.Clear();
        _openPath.Clear();
        _visibleRects.Clear();
    }

    public bool Draw(Rect? anchor = null)
    {
        if (!isOpen) return false;
        _visibleRects.Clear();
        DrawLevel(_roots, _position, 0, null);
        var evt = Event.current;
        var eventType = evt.type;
        var pointerInside = _visibleRects.Any(rect => rect.Contains(evt.mousePosition)) ||
                            anchor is { } anchorRect && anchorRect.Contains(evt.mousePosition);
        if (eventType == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            Close();
        else if (eventType is EventType.MouseMove or EventType.MouseLeaveWindow && !pointerInside)
            Close();
        else if (eventType is EventType.MouseDown or EventType.ContextClick && !pointerInside)
            Close();

        // Popup menus are an input layer. Process their own hover/submenu logic first, then
        // consume the event so docked windows and controls underneath never see it.
        if (IsInputEvent(eventType) && evt.type != EventType.Used)
            evt.Use();
        return isOpen;
    }

    internal static bool IsInputEvent(EventType type) => type is
        EventType.MouseDown or EventType.MouseUp or EventType.MouseMove or EventType.MouseDrag or
        EventType.ContextClick or EventType.ScrollWheel or EventType.MouseEnterWindow or
        EventType.MouseLeaveWindow or EventType.TouchDown or EventType.TouchUp or EventType.TouchMove or
        EventType.DragUpdated or EventType.DragPerform or EventType.DragExited or
        EventType.KeyDown or EventType.KeyUp or EventType.ValidateCommand or EventType.ExecuteCommand;

    private void DrawLevel(IReadOnlyList<PopupNode> nodes, Vector2 requestedPosition, int depth,
        Rect? parentRect)
    {
        var rowHeight = Fix64.Max(20, EditorStyles.menuItem.fixedHeight);
        var separatorHeight = Fix64.Max(7, rowHeight / 3);
        var width = Fix64.Max(190, nodes.Where(node => !node.Separator)
            .Select(node => EditorStyles.menuItem.CalcSize(new GUIContent(node.Name)).x + 64)
            .DefaultIfEmpty(190).Max());
        var height = (Fix64)6;
        foreach (var node in nodes) height += node.Separator ? separatorHeight : rowHeight;
        var maximumX = Fix64.Max(2, GUIUtility.currentViewWidth - width - 2);
        var x = parentRect is { } parent && requestedPosition.x + width > GUIUtility.currentViewWidth - 2
            ? parent.x - width + 1
            : requestedPosition.x;
        x = Fix64.Clamp(x, 2, maximumX);
        var y = Fix64.Clamp(requestedPosition.y, 2,
            Fix64.Max(2, GUIUtility.currentViewHeight - height - 2));
        var menuRect = new Rect(x, y, width, height);
        _visibleRects.Add(menuRect);
        if (Event.current.type == EventType.Repaint)
        {
            GUI.DrawRect(new Rect(menuRect.x + 3, menuRect.y + 3, menuRect.width, menuRect.height),
                EditorAppearance.palette.Shadow);
            GUI.DrawRect(menuRect, EditorAppearance.palette.PanelRaised);
            DrawBorder(menuRect, EditorAppearance.palette.Border);
        }

        var cursorY = menuRect.y + 3;
        PopupNode? child = null;
        Rect childRow = default;
        foreach (var node in nodes)
        {
            if (node.Separator)
            {
                GUI.DrawRect(new Rect(menuRect.x + 5, cursorY + separatorHeight / 2,
                    menuRect.width - 10, 1), EditorAppearance.palette.Border);
                cursorY += separatorHeight;
                continue;
            }

            var row = new Rect(menuRect.x + 3, cursorY, menuRect.width - 6, rowHeight);
            var hovered = row.Contains(Event.current.mousePosition);
            if (hovered && Event.current.type is EventType.MouseMove or EventType.MouseDown)
            {
                if (node.Children.Count > 0) OpenSubmenu(depth, node);
                else TrimOpenPath(depth);
            }

            var enabled = node.IsEnabled;
            var previousEnabled = GUI.enabled;
            GUI.enabled = enabled;
            var itemStyle = enabled ? EditorStyles.menuItem : EditorStyles.menuItemDisabled;
            var clicked = GUI.Button(row, GUIContent.none,
                itemStyle);

            if (node.On)
                GUI.Label(new Rect(row.x + 5, row.y, 16, row.height),
                    new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.Check, "Checked"));
            GUI.Label(new Rect(row.x + 27, row.y, Fix64.Max(0, row.width - 51), row.height),
                node.Name, itemStyle);
            if (node.Children.Count > 0)
                GUI.Label(new Rect(row.xMax - 17, row.y, 14, row.height), ">", itemStyle);
            GUI.enabled = previousEnabled;

            if (clicked && enabled)
            {
                if (node.Children.Count > 0) OpenSubmenu(depth, node);
                else
                {
                    var action = node.Action;
                    Close();
                    if (action is not null)
                        EditorFeatureGuard.Invoke($"GenericMenu {node.Name}", action);
                    return;
                }
            }

            if (_openPath.Count > depth && ReferenceEquals(_openPath[depth], node))
            {
                child = node;
                childRow = row;
            }
            cursorY += rowHeight;
        }

        if (child is not null)
            DrawLevel(child.Children, new Vector2(menuRect.xMax - 1, childRow.y), depth + 1, menuRect);
    }

    private static void DrawBorder(Rect rect, Color color)
    {
        GUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
        GUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
        GUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), color);
        GUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
    }

    private void OpenSubmenu(int depth, PopupNode node)
    {
        if (_openPath.Count > depth && ReferenceEquals(_openPath[depth], node)) return;
        TrimOpenPath(depth);
        _openPath.Add(node);
    }

    private void TrimOpenPath(int depth)
    {
        while (_openPath.Count > depth) _openPath.RemoveAt(_openPath.Count - 1);
    }

    private void Add(GenericMenuItem item)
    {
        var segments = item.Path.Replace('\\', '/').Split('/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var level = _roots;
        if (item.Separator)
        {
            foreach (var segment in segments)
                level = FindOrAdd(level, segment).Children;
            level.Add(PopupNode.CreateSeparator());
            return;
        }
        if (segments.Length == 0) return;
        PopupNode? leaf = null;
        foreach (var segment in segments)
        {
            leaf = FindOrAdd(level, segment);
            level = leaf.Children;
        }
        leaf!.On = item.On;
        leaf.Enabled = item.Enabled;
        leaf.Action = item.Action;
    }

    private static PopupNode FindOrAdd(List<PopupNode> nodes, string name)
    {
        var node = nodes.FirstOrDefault(candidate => !candidate.Separator && candidate.Name == name);
        if (node is not null) return node;
        node = new PopupNode(name);
        nodes.Add(node);
        return node;
    }

    private sealed class PopupNode(string name)
    {
        public string Name { get; } = name;
        public List<PopupNode> Children { get; } = [];
        public bool On { get; set; }
        public bool Enabled { get; set; } = true;
        public bool Separator { get; private init; }
        public Action? Action { get; set; }
        public bool IsEnabled => !Separator && (Children.Count == 0 ? Enabled : Children.Any(child => child.IsEnabled));

        public static PopupNode CreateSeparator() => new(string.Empty) { Separator = true, Enabled = false };
    }
}
