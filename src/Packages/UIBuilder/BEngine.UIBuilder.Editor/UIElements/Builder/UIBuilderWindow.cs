using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.UIElements.Editor;

[EditorWindowIcon("Icons/Windows/UIBuilder.png")]
public sealed class UIBuilderWindow :
    UIElementsEditorWindow<UIBuilderWindowModel, UIBuilderWindowController>, IEditorDocumentCommandHandler
{
    private static readonly Fix64 PaneHeaderHeight = 24;
    private static readonly Fix64 ViewportToolbarHeight = 44;
    private static readonly Fix64 PreviewBarHeight = 24;
    private static readonly Fix64 StatusBarHeight = 22;
    private Rect _uiViewport;
    private float _previewScale = 1;
    private int _lastGameWidth;
    private int _lastGameHeight;
    private UIBuilderHierarchyTreeView? _hierarchyTree;
    private UIBuilderLibraryTreeView? _libraryTree;
    private bool _treeViewsInitialized;
    private long _libraryRevision = -1;
    private Fix64 _leftWidth = 250;
    private Fix64 _inspectorWidth = 300;
    private Fix64 _styleSheetsHeight = 170;
    private Fix64 _hierarchyHeight = 210;
    private const int SplitterSize = 6;
    private Fix64 _inspectorContentHeight = 1280;

    public UIBuilderWindow()
    {
        titleContent = new GUIContent("UI Builder", "Icons/Windows/UIBuilder.png", "UI Builder");
        minSize = new Vector2(960, 560);
        position = new Rect(80, 60, 1280, 760);
        uiElementContainer.interactable = false;
        uiElementContainer.backgroundColor = new UIColor(46, 46, 46);
        wantsMouseMove = true;
        wantsMouseEnterLeaveWindow = true;
    }

    protected override UIBuilderWindowModel CreateWindowModel(EditorWindowMvcContext context) =>
        new(context.WindowId);

    protected override UIBuilderWindowController CreateWindowController(
        EditorWindowMvcContext context,
        UIBuilderWindowModel windowModel) => new(
            context,
            windowModel,
            EditorServices.GetRequiredService<FileUtility>(),
            EditorServices.GetRequiredService<PathUtility>());

    protected override void OnEnable()
    {
        (_lastGameWidth, _lastGameHeight) = GameResolution();
        EnsureTreeViews();
    }

    protected override void OnDisable()
    {
        model.PreviewDocument?.CancelInteraction();
        _uiViewport = default;
    }

    protected override void OnLostFocus() => model.PreviewDocument?.CancelInteraction();

    protected override void Update()
    {
        if (_libraryRevision != UxmlFactoryRegistry.revision)
        {
            _libraryRevision = UxmlFactoryRegistry.revision;
            _libraryTree?.Reload();
            Repaint();
        }
        var target = GameResolution();
        if (target.Width == _lastGameWidth && target.Height == _lastGameHeight) return;
        (_lastGameWidth, _lastGameHeight) = target;
        Repaint();
    }

    [MenuItem("Window/UIElement/UI Builder", false, 230)]
    public static void Open() => GetWindow<UIBuilderWindow>("UI Builder", true).ShowAuxWindow();

    public static void Open(string path)
    {
        var window = GetWindow<UIBuilderWindow>("UI Builder", true);
        window.controller.LoadDocument(path);
        window.ShowAuxWindow();
    }

    protected override void OnGUI()
    {
        HandleKeyboard();
        var width = Fix64.Max(1, GUIUtility.currentViewWidth);
        var height = Fix64.Max(1, GUIUtility.currentViewHeight);
        var leftWidth = _leftWidth = Fix64.Clamp(_leftWidth, 180, Fix64.Max(180, width - 500));
        var inspectorWidth = _inspectorWidth = Fix64.Clamp(_inspectorWidth, 220,
            Fix64.Max(220, width - leftWidth - 280));
        var viewportWidth = Fix64.Max(1, width - leftWidth - inspectorWidth - SplitterSize * 2);

        // Capture splitters before child controls can consume a drag crossing their bounds.
        _leftWidth += DrawSplitter(new Rect(leftWidth, 0, SplitterSize, height), true);
        _inspectorWidth -= DrawSplitter(new Rect(leftWidth + viewportWidth + SplitterSize, 0,
            SplitterSize, height), true);

        GUI.enabled = !model.PreviewMode && EditorAssetWritePolicy.CanWrite;
        DrawLeftColumn(new Rect(0, 0, leftWidth, height));
        GUI.enabled = true;
        DrawViewport(new Rect(leftWidth + SplitterSize, 0, viewportWidth, height));
        GUI.enabled = !model.PreviewMode && EditorAssetWritePolicy.CanWrite;
        DrawInspector(new Rect(leftWidth + viewportWidth + SplitterSize * 2, 0, inspectorWidth, height));
        GUI.enabled = true;
    }

    public override void AddItemsToMenu(GenericMenu menu)
    {
        menu.AddItem(new GUIContent("New"), false, controller.NewDocument);
        menu.AddItem(new GUIContent("Open..."), false, controller.OpenDocument);
        menu.AddItem(new GUIContent("Save"), false, controller.SaveDocument);
        menu.AddItem(new GUIContent("Save As..."), false, controller.SaveDocumentAs);
        menu.AddSeparator(string.Empty);
        if (model.CanUndo && !model.PreviewMode) menu.AddItem(new GUIContent("Undo"), false, controller.Undo);
        else menu.AddDisabledItem(new GUIContent("Undo"));
        if (model.CanRedo && !model.PreviewMode) menu.AddItem(new GUIContent("Redo"), false, controller.Redo);
        else menu.AddDisabledItem(new GUIContent("Redo"));
        menu.AddSeparator(string.Empty);
        menu.AddItem(new GUIContent("Preview"), model.PreviewMode, () => controller.SetPreviewMode(!model.PreviewMode));
    }

    private void DrawToolbar(Rect area)
    {
        GUILayout.BeginArea(area);
        GUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(40));
        var iconOptions = new[] { GUILayout.Width(32), GUILayout.Height(36) };
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.New, "New UI document", iconOptions))
            controller.NewDocument();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Browse, "Open UI document", iconOptions))
            controller.OpenDocument();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Save, "Save UI document", iconOptions))
            controller.SaveDocument();
        GUI.enabled = model.CanUndo && !model.PreviewMode;
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Undo, "Undo (Ctrl+Z)", iconOptions)) controller.Undo();
        GUI.enabled = model.CanRedo && !model.PreviewMode;
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Redo, "Redo (Ctrl+Y)", iconOptions)) controller.Redo();
        GUI.enabled = !model.PreviewMode;
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Duplicate,
                "Duplicate selected element", iconOptions))
            controller.DuplicateSelected();
        if (area.width >= 650)
        {
            if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Up,
                    "Move selected element up", iconOptions))
                controller.MoveSelected(-1);
            if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Move,
                    "Move selected element down", iconOptions))
                controller.MoveSelected(1);
        }
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Delete,
                "Delete selected element", iconOptions))
            controller.DeleteSelected();
        GUI.enabled = true;
        GUILayout.FlexibleSpace();
        var target = GameResolution();
        if (area.width >= 530)
            GUILayout.Label($"Game {target.Width} x {target.Height}", EditorStyles.miniLabel,
                GUILayout.Width(116));
        if (area.width >= 430)
            GUILayout.Label($"{_previewScale:P0}", EditorStyles.centeredMiniLabel, GUILayout.Width(42));
        using (new EditorGUI.DisabledScope(!EditorTool.IsAvailable("html-converter")))
        if (EditorToolbar.IconButton("Icons/Toolbar/Html.png",
                "Open HTML converter", iconOptions))
            EditorTool.Open("html-converter");
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private void DrawLeftColumn(Rect area)
    {
        var styleSheetsHeight = _styleSheetsHeight = Fix64.Clamp(_styleSheetsHeight, 90,
            Fix64.Max(90, area.height - 250));
        var hierarchyHeight = _hierarchyHeight = Fix64.Clamp(_hierarchyHeight, 90,
            Fix64.Max(90, area.height - styleSheetsHeight - 150));
        var libraryHeight = Fix64.Max(1, area.height - styleSheetsHeight - hierarchyHeight - SplitterSize * 2);
        _styleSheetsHeight += DrawSplitter(new Rect(area.x, area.y + styleSheetsHeight,
            area.width, SplitterSize), false);
        _hierarchyHeight += DrawSplitter(new Rect(area.x,
            area.y + styleSheetsHeight + hierarchyHeight + SplitterSize, area.width, SplitterSize), false);
        DrawStyleSheets(new Rect(area.x, area.y, area.width, styleSheetsHeight));
        DrawHierarchyPane(new Rect(area.x, area.y + styleSheetsHeight + SplitterSize,
            area.width, hierarchyHeight));
        DrawLibrary(new Rect(area.x, area.y + styleSheetsHeight + hierarchyHeight + SplitterSize * 2,
            area.width, libraryHeight));
    }

    private Fix64 DrawSplitter(Rect rect, bool vertical)
    {
        GUI.DrawRect(rect, EditorStyles.separator.normal.backgroundColor);
        EditorGUIUtility.AddCursorRect(rect, vertical ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);
        var id = GUIUtility.GetControlID(FocusType.Passive);
        var current = Event.current;
        if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(current.mousePosition))
        {
            GUIUtility.hotControl = id;
            current.Use();
        }
        else if (current.type == EventType.MouseDrag && GUIUtility.hotControl == id)
        {
            var delta = vertical ? current.delta.x : current.delta.y;
            current.Use();
            Repaint();
            return delta;
        }
        else if (current.type == EventType.MouseUp && GUIUtility.hotControl == id)
        {
            GUIUtility.hotControl = 0;
            current.Use();
        }
        return 0;
    }

    private void DrawStyleSheets(Rect area)
    {
        DrawPane(area, "StyleSheets");
        var toolbar = new Rect(area.x, area.y + PaneHeaderHeight, area.width, 28);
        GUI.Box(toolbar, string.Empty, EditorStyles.toolbar);
        var add = new Rect(toolbar.x + 3, toolbar.y + 3, 24, 22);
        if (GUI.Button(add, EditorGUIUtility.IconContent(EditorBuiltinIcons.Toolbar.AddDropdown,
                "Add StyleSheet"), EditorStyles.toolbarButton))
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Add Existing Style Sheet..."), false,
                controller.AddExistingStyleSheet);
            menu.AddItem(new GUIContent("Create New Style Sheet..."), false,
                controller.CreateStyleSheet);
            menu.DropDown(add);
        }
        var previous = GUI.enabled;
        GUI.enabled = previous && model.SelectedStyleSheetIndex >= 0 &&
                      model.SelectedStyleSheetIndex < model.DocumentRoot.styleSheets.Count;
        if (GUI.Button(new Rect(toolbar.x + 28, toolbar.y + 3, 24, 22),
                EditorGUIUtility.IconContent(EditorBuiltinIcons.Toolbar.Minus, "Remove StyleSheet"),
                EditorStyles.toolbarButton))
            controller.RemoveSelectedStyleSheet();
        GUI.enabled = previous;
        GUI.Label(new Rect(toolbar.x + 58, toolbar.y + 3, Fix64.Max(1, toolbar.width - 62), 22),
            "Document StyleSheets", EditorStyles.miniLabel);

        var list = new Rect(area.x + 4, toolbar.yMax + 4,
            Fix64.Max(1, area.width - 8), Fix64.Max(1, area.yMax - toolbar.yMax - 8));
        if (model.DocumentRoot.styleSheets.Count == 0)
        {
            GUI.Label(list, "List is Empty", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        model.StyleSheetsScroll = GUI.BeginScrollView(list, model.StyleSheetsScroll,
            new Rect(0, 0, Fix64.Max(1, list.width - 12), Fix64.Max(list.height, model.DocumentRoot.styleSheets.Count * 22)));
        for (var index = 0; index < model.DocumentRoot.styleSheets.Count; index++)
        {
            var sheet = model.DocumentRoot.styleSheets[index];
            var title = string.IsNullOrWhiteSpace(sheet.name) ? $"StyleSheet {index + 1}" : sheet.name;
            var row = new Rect(0, index * 22, Fix64.Max(1, list.width - 12), 22);
            var style = index == model.SelectedStyleSheetIndex
                ? EditorStyles.treeViewRowSelected
                : EditorStyles.treeViewRow;
            if (GUI.Button(row, new GUIContent(title, EditorBuiltinIcons.Assets.Style, sheet.assetPath), style))
                model.SelectedStyleSheetIndex = index;
        }
        GUI.EndScrollView();
    }

    private void DrawHierarchyPane(Rect area)
    {
        DrawPane(area, "Hierarchy");
        var viewport = new Rect(area.x, area.y + PaneHeaderHeight, area.width,
            Fix64.Max(1, area.height - PaneHeaderHeight));
        EnsureTreeViews();
        _hierarchyTree!.OnGUI(viewport);
    }

    private void DrawLibrary(Rect area)
    {
        DrawPane(area, "Library");
        var tabs = new Rect(area.x + 4, area.y + PaneHeaderHeight + 3, area.width - 8, 24);
        if (GUI.Toggle(new Rect(tabs.x, tabs.y, tabs.width / 2, tabs.height), !model.LibraryProjectControls, "Standard",
                EditorStyles.toolbarButton)) controller.SetLibraryTab(false);
        if (GUI.Toggle(new Rect(tabs.x + tabs.width / 2, tabs.y, tabs.width / 2, tabs.height), model.LibraryProjectControls, "Project",
                EditorStyles.toolbarButton)) controller.SetLibraryTab(true);
        var searchArea = new Rect(area.x + 4, tabs.yMax + 3, area.width - 8, 25);
        GUILayout.BeginArea(searchArea);
        var search = EditorToolbar.SearchField(model.LibrarySearch, "Search controls",
            GUILayout.ExpandWidth(true));
        if (search != model.LibrarySearch) model.LibrarySearch = search;
        GUILayout.EndArea();

        var viewport = new Rect(area.x, searchArea.yMax + 3, area.width,
            Fix64.Max(1, area.yMax - searchArea.yMax - 3));
        EnsureTreeViews();
        _libraryTree!.searchString = model.LibrarySearch;
        _libraryTree.OnGUI(viewport);
    }

    private void DrawViewport(Rect area)
    {
        DrawPane(area, "Viewport", drawBackground: false);
        var toolbar = new Rect(area.x, area.y + PaneHeaderHeight, area.width, ViewportToolbarHeight);
        DrawToolbar(toolbar);
        var previewY = area.yMax - PreviewBarHeight - StatusBarHeight;
        var available = new Rect(area.x, toolbar.yMax, area.width,
            Fix64.Max(1, previewY - toolbar.yMax));
        DrawCanvas(available);
        DrawPreviewBar(new Rect(area.x, previewY, area.width, PreviewBarHeight));
        DrawStatusBar(new Rect(area.x, previewY + PreviewBarHeight, area.width, StatusBarHeight));
    }

    private void DrawCanvas(Rect available)
    {
        var target = GameResolution();
        var aspect = target.Width / (float)target.Height;
        var fitWidth = Math.Max(1, Math.Min((float)available.width - 32,
            ((float)available.height - 32) * aspect));
        var width = fitWidth;
        var height = width / aspect;
        _previewScale = width / target.Width;
        var viewport = new Rect(
            available.x + (available.width - (Fix64)width) / 2,
            available.y + (available.height - (Fix64)height) / 2,
            (Fix64)width,
            (Fix64)height);
        if (!_uiViewport.Equals(viewport))
        {
            _uiViewport = viewport;
            Repaint();
        }
        uiElementContainer.position = _uiViewport;
        uiElementContainer.contentSize = new Vector2(target.Width, target.Height);
        uiElementContainer.SetRootVisualElement(model.CanvasRoot);
        // The retained tree is rendered before IMGUI; keep its viewport uncovered.
        var background = EditorStyles.viewBackground.normal.backgroundColor;
        GUI.DrawRect(new Rect(available.x, available.y, available.width, viewport.y - available.y), background);
        GUI.DrawRect(new Rect(available.x, viewport.yMax, available.width, available.yMax - viewport.yMax), background);
        GUI.DrawRect(new Rect(available.x, viewport.y, viewport.x - available.x, viewport.height), background);
        GUI.DrawRect(new Rect(viewport.xMax, viewport.y, available.xMax - viewport.xMax, viewport.height), background);
        DrawBorder(_uiViewport, EditorStyles.separator.normal.backgroundColor);
        HandleViewportInput(target.Width, target.Height);
        if (!model.PreviewMode) DrawSelectionOutline(target.Width, target.Height);
    }

    private void HandleViewportInput(int targetWidth, int targetHeight)
    {
        var current = Event.current;
        if (model.PreviewDocument is { } preview)
        {
            var previewPoint = new Vector2(
                (current.mousePosition.x - _uiViewport.x) * targetWidth / _uiViewport.width,
                (current.mousePosition.y - _uiViewport.y) * targetHeight / _uiViewport.height);
            var inside = _uiViewport.Contains(current.mousePosition);
            switch (current.type)
            {
                case EventType.MouseMove:
                case EventType.MouseDrag:
                    preview.DispatchPointerMove(previewPoint, targetWidth, targetHeight);
                    Repaint();
                    if (inside) current.Use();
                    break;
                case EventType.MouseDown when current.button == 0:
                    if (!inside) { preview.CancelInteraction(); break; }
                    GUIUtility.keyboardControl = 0;
                    Focus();
                    preview.DispatchPointerDown(previewPoint, targetWidth, targetHeight);
                    current.Use();
                    Repaint();
                    break;
                case EventType.MouseUp when current.button == 0:
                    preview.DispatchPointerUp(previewPoint, targetWidth, targetHeight);
                    if (inside) current.Use();
                    Repaint();
                    break;
                case EventType.ScrollWheel when inside:
                    preview.DispatchScroll(previewPoint, (float)current.delta.y * 48, targetWidth, targetHeight);
                    current.Use();
                    Repaint();
                    break;
                case EventType.MouseLeaveWindow:
                    preview.CancelInteraction();
                    Repaint();
                    break;
            }
            return;
        }
        if (current.type != EventType.MouseDown || current.button != 0 ||
            !_uiViewport.Contains(current.mousePosition)) return;
        var point = new Vector2(
            (current.mousePosition.x - _uiViewport.x) * targetWidth / _uiViewport.width,
            (current.mousePosition.y - _uiViewport.y) * targetHeight / _uiViewport.height);
        var target = UIRenderListBuilder.Build(model.DocumentRoot, targetWidth, targetHeight).Pick(point);
        if (target is not null) controller.Select(target);
        Focus();
        current.Use();
    }

    private void DrawSelectionOutline(int targetWidth, int targetHeight)
    {
        var renderList = UIRenderListBuilder.Build(model.DocumentRoot, targetWidth, targetHeight);
        if (!renderList.TryGetVisibleRect(model.Selected, out var selected)) return;
        var rect = new Rect(
            _uiViewport.x + selected.X * _uiViewport.width / targetWidth,
            _uiViewport.y + selected.Y * _uiViewport.height / targetHeight,
            selected.Width * _uiViewport.width / targetWidth,
            selected.Height * _uiViewport.height / targetHeight);
        DrawBorder(rect, EditorStyles.selectionRect.normal.backgroundColor);
    }

    private static void DrawBorder(Rect rect, Color color)
    {
        GUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
        GUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
        GUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), color);
        GUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
    }

    private static (int Width, int Height) GameResolution() =>
        (Math.Max(1, Screen.width), Math.Max(1, Screen.height));

    private void DrawInspector(Rect area)
    {
        DrawPane(area, "Inspector");
        var viewport = new Rect(area.x, area.y + PaneHeaderHeight, area.width,
            Fix64.Max(1, area.height - PaneHeaderHeight));
        var contentHeight = Fix64.Max(viewport.height, _inspectorContentHeight);
        model.InspectorScroll = GUI.BeginScrollView(viewport, model.InspectorScroll,
            new Rect(0, 0, Fix64.Max(1, viewport.width - 14), contentHeight));
        GUILayout.BeginArea(new Rect(0, 0, Fix64.Max(1, viewport.width - 14), contentHeight));
        try
        {
            var editable = GUI.enabled;
            if (DrawInspectorSection("Element", model.Selected.GetType().Name))
            {
                GUI.enabled = false;
                EditorGUILayout.TextField("Type", model.Selected.GetType().Name);
                GUI.enabled = editable;
                EditText("Name", model.Selected.name, value => model.Selected.name = value);
                EditText("Tooltip", model.Selected.tooltip, value => model.Selected.tooltip = value);
                EditText("Binding Path", model.Selected.bindingPath, value => model.Selected.bindingPath = value);
                EditBool("Visible", model.Selected.visible, value => model.Selected.visible = value);
                EditBool("Enabled", model.Selected.enabledSelf, model.Selected.SetEnabled);
                DrawControlFields();
            }

            if (DrawInspectorSection("StyleSheet", "StyleSheet"))
            {
                GUI.enabled = false;
                EditorGUILayout.IntField("Attached", model.DocumentRoot.styleSheets.Count);
                GUI.enabled = editable;
            }

            if (DrawInspectorSection("Style Class List", "Style Class List"))
                EditText("Classes", string.Join(' ', model.Selected.classList), controller.SetSelectedClasses);

            _ = DrawInspectorSection("Matching Selectors", "Matching Selectors");

            if (DrawInspectorSection("Inline Styles", "Inline Styles")) DrawInlineStyleFields();
        }
        finally
        {
            _inspectorContentHeight = Fix64.Max(1, GUILayout.CurrentContentHeight + 8);
            GUILayout.EndArea();
            GUI.EndScrollView();
        }
    }

    private void DrawControlFields()
    {
        if (model.Selected is TextElement text) EditText("Text", text.text, value => text.text = value);
        if (model.Selected is Vector2Field vector2)
        {
            var next = EditorGUILayout.Vector2Field("Value", vector2.value);
            if (next != vector2.value) controller.Change("Vector2 Value", () => vector2.value = next);
        }
        else if (model.Selected is Vector4Field vector4)
        {
            var next = EditorGUILayout.Vector4Field("Value", vector4.value);
            if (next != vector4.value) controller.Change("Vector4 Value", () => vector4.value = next);
        }
        else if (model.Selected is TextField textField)
        {
            EditText("Label", textField.label, value => textField.label = value);
            EditText("Value", textField.value, value => textField.value = value);
            EditBool("Multiline", textField.multiline, value => textField.multiline = value);
            EditInt("Max Length (-1 unlimited)", textField.maxLength, value => textField.maxLength = Math.Max(-1, value));
            EditText("Placeholder", textField.placeholderText, value => textField.placeholderText = value);
            EditBool("Read Only", textField.isReadOnly, value => textField.isReadOnly = value);
            EditBool("Password", textField.isPasswordField, value => textField.isPasswordField = value);
            EditBool("Scroll To End", textField.scrollToEnd, value => textField.scrollToEnd = value);
        }
        else if (model.Selected is Toggle toggle)
        {
            EditText("Label", toggle.label, value => toggle.label = value);
            EditBool("Value", toggle.value, value => toggle.value = value);
        }
        else if (model.Selected is IntegerField integer)
            EditInt("Value", integer.value, value => integer.value = value);
        else if (model.Selected is FloatField number)
            EditFloat("Value", number.value, value => number.value = value);
        else if (model.Selected is Slider slider)
        {
            EditFloat("Low Value", slider.lowValue, value => slider.lowValue = value);
            EditFloat("High Value", slider.highValue, value => slider.highValue = value);
            EditFloat("Value", slider.value, value => slider.value = value);
        }
        else if (model.Selected is ProgressBar progress)
        {
            EditText("Title", progress.title, value => progress.title = value);
            EditFloat("Value", progress.value, value => progress.value = value);
        }
        else if (model.Selected is Image image)
            EditText("Source", image.sourcePath, value => image.sourcePath = value);
    }

    private void DrawInlineStyleFields()
    {
        var style = model.Selected.style;
        if (DrawInspectorSection("Display", "Display"))
        {
            EditEnum("Display", style.display, value => style.display = value);
            EditEnum("Overflow", style.overflow, value => style.overflow = value);
        }
        if (DrawInspectorSection("Position", "Position"))
        {
            EditEnum("Position", style.position, value => style.position = value);
            EditFloat("Left", style.left, value => style.left = value);
            EditFloat("Top", style.top, value => style.top = value);
            EditFloat("Right", style.right, value => style.right = value);
            EditFloat("Bottom", style.bottom, value => style.bottom = value);
        }
        if (DrawInspectorSection("Flex", "Flex"))
        {
            EditEnum("Direction", style.flexDirection, value => style.flexDirection = value);
            EditFloat("Grow", style.flexGrow, value => style.flexGrow = value);
            EditFloat("Shrink", style.flexShrink, value => style.flexShrink = value);
        }
        if (DrawInspectorSection("Align", "Align"))
        {
            EditEnum("Align Items", style.alignItems, value => style.alignItems = value);
            EditEnum("Justify", style.justifyContent, value => style.justifyContent = value);
        }
        if (DrawInspectorSection("Size", "Size"))
        {
            EditFloat("Width", style.width, value => style.width = value);
            EditFloat("Height", style.height, value => style.height = value);
            EditFloat("Min Width", style.minWidth, value => style.minWidth = value);
            EditFloat("Min Height", style.minHeight, value => style.minHeight = value);
            EditFloat("Max Width", style.maxWidth, value => style.maxWidth = value);
            EditFloat("Max Height", style.maxHeight, value => style.maxHeight = value);
        }
        if (DrawInspectorSection("Margin & Padding", "Margin & Padding"))
        {
            EditSpacing("Margin", style.marginLeft, style.marginTop, style.marginRight, style.marginBottom,
                style.SetMargin);
            EditSpacing("Padding", style.paddingLeft, style.paddingTop, style.paddingRight, style.paddingBottom,
                style.SetPadding);
        }
        if (DrawInspectorSection("Text", "Text"))
        {
            EditFloat("Font Size", style.fontSize, value => style.fontSize = value);
            EditText("SDF Font Asset", style.fontAsset, value => style.fontAsset = value);
            EditFloat("Outline Width", style.textOutlineWidth, value => style.textOutlineWidth = value);
            EditColor("Outline Color", style.textOutlineColor, value => style.textOutlineColor = value);
            EditColor("Color", style.color, value => style.color = value);
        }
        if (DrawInspectorSection("Background", "Background"))
            EditColor("Color", style.backgroundColor, value => style.backgroundColor = value);
        if (DrawInspectorSection("Border", "Border"))
        {
            EditFloat("Width", style.borderWidth, value => style.borderWidth = value);
            EditColor("Color", style.borderColor, value => style.borderColor = value);
        }
    }

    private bool DrawInspectorSection(string key, string label)
    {
        var current = model.InspectorFoldouts.GetValueOrDefault(key);
        var next = EditorGUILayout.Foldout(current, label, EditorStyles.inspectorTitlebar);
        model.InspectorFoldouts[key] = next;
        return next;
    }

    private void EditSpacing(
        string label,
        float left,
        float top,
        float right,
        float bottom,
        Action<float, float, float, float> apply)
    {
        var nextLeft = EditorGUILayout.FloatField(label + " Left", left);
        var nextTop = EditorGUILayout.FloatField(label + " Top", top);
        var nextRight = EditorGUILayout.FloatField(label + " Right", right);
        var nextBottom = EditorGUILayout.FloatField(label + " Bottom", bottom);
        if (Math.Abs(nextLeft - left) <= 0.0001f && Math.Abs(nextTop - top) <= 0.0001f &&
            Math.Abs(nextRight - right) <= 0.0001f && Math.Abs(nextBottom - bottom) <= 0.0001f) return;
        controller.Change(label, () => apply(nextLeft, nextTop, nextRight, nextBottom));
    }

    private void DrawPreviewBar(Rect area)
    {
        GUI.Box(area, string.Empty, EditorStyles.toolbar);
        var tabWidth = Fix64.Min(150, area.width / 2);
        if (GUI.Button(new Rect(area.x, area.y, tabWidth, area.height), "Edit",
                !model.PreviewMode ? EditorStyles.toolbarIconButtonSelected : EditorStyles.toolbarButton))
            controller.SetPreviewMode(false);
        if (GUI.Button(new Rect(area.x + tabWidth, area.y, tabWidth, area.height),
                new GUIContent("Preview", EditorBuiltinIcons.Toolbar.Play, "Preview (Ctrl+P)"),
                model.PreviewMode ? EditorStyles.toolbarIconButtonSelected : EditorStyles.toolbarButton))
            controller.SetPreviewMode(true);
    }

    private void DrawStatusBar(Rect area)
    {
        GUI.Box(area, string.Empty, EditorStyles.statusBar);
        GUI.Label(new Rect(area.x + 6, area.y, 62, area.height),
            model.Dirty ? "Modified" : "Saved", EditorStyles.miniBoldLabel);
        var path = string.IsNullOrWhiteSpace(model.AssetPath) ? "New document" : model.AssetPath;
        GUI.Label(new Rect(area.x + 70, area.y, Fix64.Max(1, area.width - 180), area.height),
            path, EditorStyles.miniLabel);
        GUI.Label(new Rect(area.xMax - 108, area.y, 102, area.height), model.Status,
            EditorStyles.miniLabel);
    }

    private static void DrawPane(Rect area, string title, bool drawBackground = true)
    {
        if (drawBackground) GUI.Box(area, string.Empty, EditorStyles.viewBackground);
        GUI.Box(new Rect(area.x, area.y, area.width, PaneHeaderHeight), string.Empty, EditorStyles.toolbar);
        GUI.Label(new Rect(area.x + 6, area.y, Fix64.Max(1, area.width - 12), PaneHeaderHeight),
            title, EditorStyles.boldLabel);
    }

    private void HandleKeyboard()
    {
        var evt = Event.current;
        if (evt.type != EventType.KeyDown || (focusedWindow is not null && !hasFocus)) return;
        var action = evt.control || evt.command;
        if (action && evt.keyCode == KeyCode.P) controller.SetPreviewMode(!model.PreviewMode);
        else if (action && evt.keyCode == KeyCode.S)
        {
            if (evt.shift) controller.SaveDocumentAs(); else controller.SaveDocument();
        }
        else if (model.PreviewDocument is { } preview)
        {
            if (evt.keyCode == KeyCode.Escape) controller.SetPreviewMode(false);
            else
            {
                var handled = preview.DispatchKeyDown(evt.keyCode, evt.shift);
                if (!handled && !action && !evt.alt && evt.character != '\0')
                    preview.DispatchTextInput(evt.character.ToString());
            }
            // Preview owns keyboard input, including keys also used by scene tools.
        }
        else if (action && evt.keyCode is KeyCode.Z or KeyCode.Y)
        {
            GUIUtility.keyboardControl = 0;
            if (evt.keyCode == KeyCode.Y || evt.shift) controller.Redo(); else controller.Undo();
        }
        else if (EditorGUIUtility.editingTextField) return;
        else if (action && evt.keyCode == KeyCode.N) controller.NewDocument();
        else if (action && evt.keyCode == KeyCode.O) controller.OpenDocument();
        else if (action && evt.keyCode == KeyCode.D) controller.DuplicateSelected();
        else if (action && evt.keyCode == KeyCode.C) controller.CopySelected();
        else if (action && evt.keyCode == KeyCode.X) controller.CutSelected();
        else if (action && evt.keyCode == KeyCode.V) controller.Paste();
        else if (evt.keyCode == KeyCode.Delete) controller.DeleteSelected();
        else if (evt.keyCode == KeyCode.F2) RenameSelected();
        else if (evt.alt && evt.keyCode == KeyCode.UpArrow) controller.MoveSelected(-1);
        else if (evt.alt && evt.keyCode == KeyCode.DownArrow) controller.MoveSelected(1);
        else return;
        evt.Use();
        Repaint();
    }

    internal void RenameSelected()
    {
        EnsureTreeViews();
        _hierarchyTree!.RenameSelected();
    }

    public bool TryGetDocumentCommand(MainMenuCommand command, out Action? execute)
    {
        execute = command switch
        {
            MainMenuCommand.SaveScene => controller.SaveDocument,
            MainMenuCommand.Undo when model.CanUndo && !model.PreviewMode => controller.Undo,
            MainMenuCommand.Redo when model.CanRedo && !model.PreviewMode => controller.Redo,
            MainMenuCommand.Copy => controller.CopySelected,
            MainMenuCommand.Paste when controller.CanPaste && !model.PreviewMode => controller.Paste,
            MainMenuCommand.Duplicate when model.Selected.parent is not null && !model.PreviewMode => controller.DuplicateSelected,
            MainMenuCommand.Delete when model.Selected.parent is not null && !model.PreviewMode => controller.DeleteSelected,
            MainMenuCommand.Rename when !model.PreviewMode => RenameSelected,
            _ => null
        };
        return command is MainMenuCommand.SaveScene or MainMenuCommand.Undo or MainMenuCommand.Redo or
            MainMenuCommand.Copy or MainMenuCommand.Paste or MainMenuCommand.Duplicate or
            MainMenuCommand.Delete or MainMenuCommand.Rename;
    }

    private void EditText(string label, string current, Action<string> apply)
    {
        var next = EditorGUILayout.TextField(label, current);
        if (!next.Equals(current, StringComparison.Ordinal)) controller.Change(label, () => apply(next));
    }

    private void EditFloat(string label, float current, Action<float> apply)
    {
        var next = EditorGUILayout.FloatField(label, current);
        if (!next.Equals(current)) controller.Change(label, () => apply(next));
    }

    private void EditInt(string label, int current, Action<int> apply)
    {
        var next = EditorGUILayout.IntField(label, current);
        if (next != current) controller.Change(label, () => apply(next));
    }

    private void EditBool(string label, bool current, Action<bool> apply)
    {
        var next = EditorGUILayout.Toggle(label, current);
        if (next != current) controller.Change(label, () => apply(next));
    }

    private void EditEnum<T>(string label, T current, Action<T> apply) where T : struct, Enum
    {
        var next = (T)EditorGUILayout.EnumPopup(label, current);
        if (!EqualityComparer<T>.Default.Equals(next, current)) controller.Change(label, () => apply(next));
    }

    private void EditColor(string label, UIColor? current, Action<UIColor?> apply)
    {
        EditorGUI.BeginChangeCheck();
        var converted = ToUiColor(EditorGUILayout.ColorField(label, ToEngineColor(current)));
        if (EditorGUI.EndChangeCheck()) controller.Change(label, () => apply(converted));
    }

    private static Color ToEngineColor(UIColor? value) => value is { } color
        ? new Color((Fix64)(color.R / 255f), (Fix64)(color.G / 255f),
            (Fix64)(color.B / 255f), (Fix64)(color.A / 255f))
        : new Color(0, 0, 0, 0);

    private static UIColor ToUiColor(Color value) => new(
        ToByte(value.r), ToByte(value.g), ToByte(value.b), ToByte(value.a));

    private static byte ToByte(Fix64 value) =>
        (byte)Math.Clamp((int)Math.Round((double)value * 255), 0, 255);

    private void EnsureTreeViews()
    {
        if (_hierarchyTree is null)
            _hierarchyTree = new UIBuilderHierarchyTreeView(this, model.HierarchyTreeState);
        if (_libraryTree is null)
            _libraryTree = new UIBuilderLibraryTreeView(this, model.LibraryTreeState);
        if (_treeViewsInitialized) return;
        _hierarchyTree.Reload();
        _hierarchyTree.ExpandAll();
        _libraryTree.Reload();
        _libraryTree.ExpandAll();
        _treeViewsInitialized = true;
        SyncHierarchySelection();
    }

    internal void RefreshLibraryTree()
    {
        EnsureTreeViews();
        _libraryTree!.Reload();
    }

    internal void RefreshBuilderTrees(bool expandAll = false)
    {
        EnsureTreeViews();
        _hierarchyTree!.Reload();
        if (expandAll) _hierarchyTree.ExpandAll();
        SyncHierarchySelection();
    }

    internal void SyncHierarchySelection()
    {
        if (_hierarchyTree is null) return;
        _hierarchyTree.SetSelection([model.GetElementId(model.Selected)],
            UnityEditor.IMGUI.Controls.TreeViewSelectionOptions.RevealAndFrame);
    }

    internal static VisualElement CreateDefaultDocument()
    {
        var root = new VisualElement { name = "Root" };
        root.style.flexGrow = 1;
        root.style.SetPadding(24);
        var title = new Label("BEngine UI") { name = "Title" };
        title.style.fontSize = 24;
        title.style.height = 40;
        root.Add(title);
        root.Add(new Button(text: "Start") { name = "StartButton" });
        return root;
    }

    internal static string ResolveAssetPath(string path)
    {
        var paths = EditorServices.GetRequiredService<PathUtility>();
        var platform = EditorServices.GetRequiredService<PlatformUtility>();
        if (paths.IsPathRooted(path)) return paths.GetFullPath(path);
        var root = paths.GetDirectoryName(paths.GetFullPath(Application.dataPath)) ?? platform.CurrentDirectory;
        return paths.GetFullPath(paths.Combine(root, path.Replace('/', paths.DirectorySeparatorChar)));
    }

    internal static string ToProjectPath(string path)
    {
        var paths = EditorServices.GetRequiredService<PathUtility>();
        var platform = EditorServices.GetRequiredService<PlatformUtility>();
        var root = paths.GetDirectoryName(paths.GetFullPath(Application.dataPath)) ?? platform.CurrentDirectory;
        return paths.GetRelativePath(root, path).Replace('\\', '/');
    }
}
