using BEngine.Editor;

namespace BEngine.UIElements.Editor;

[EditorWindowIcon("Icons/Windows/UIBuilder.png")]
internal sealed class UIBuilderWindow : EditorWindow
{
    private VisualElement _documentRoot = CreateDefaultDocument();
    private VisualElement _selected;
    private string _assetPath = string.Empty;
    private bool _dirty;

    public UIBuilderWindow()
    {
        _selected = _documentRoot;
        titleContent = new GUIContent("UI Builder", "Icons/Windows/UIBuilder.png", "UI Builder");
        minSize = new Vector2(760, 420); position = new Rect(120, 90, 1000, 650);
    }

    [MenuItem("Window/UI Builder", false, 230)]
    public static void Open() => GetWindow<UIBuilderWindow>("UI Builder", true).ShowAuxWindow();
    public static void Open(string path) { var window = GetWindow<UIBuilderWindow>("UI Builder", true); window.LoadDocument(path); }

    protected override void OnGUI()
    {
        GUILayout.BeginHorizontal();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.New, "New UI document")) NewDocument();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Save, "Save UI document")) SaveDocument();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Html, "Open HTML converter")) HtmlConverterWindow.Open();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Container, "Add container"))
            AddElement(new VisualElement { name = "VisualElement" });
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Label, "Add label"))
            AddElement(new Label("Text") { name = "Label" });
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Button, "Add button"))
            AddElement(new Button(text: "Button") { name = "Button" });
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Field, "Add text field"))
            AddElement(new TextField { name = "TextField" });
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(230));
        GUILayout.Label("Hierarchy", EditorStyles.boldLabel);
        DrawHierarchy(_documentRoot, 0);
        GUILayout.EndVertical();
        GUILayout.BeginVertical();
        GUILayout.Label("GPU Preview", EditorStyles.boldLabel);
        DrawPreview(_documentRoot, 0);
        GUILayout.EndVertical();
        GUILayout.BeginVertical(GUILayout.Width(300));
        DrawInspector();
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label($"{(_dirty ? "Modified" : "Saved")} | {(string.IsNullOrWhiteSpace(_assetPath) ? "New document" : _assetPath)}",
            EditorStyles.miniLabel);
    }

    public override void AddItemsToMenu(GenericMenu menu)
    {
        menu.AddItem(new GUIContent("Save"), false, SaveDocument);
        menu.AddItem(new GUIContent("New"), false, NewDocument);
    }

    private void DrawHierarchy(VisualElement element, int depth)
    {
        GUILayout.BeginHorizontal(); GUILayout.Space(depth * 14);
        var title = string.IsNullOrWhiteSpace(element.name) ? element.GetType().Name : element.name;
        if (GUILayout.Button(new GUIContent(title, EditorBuiltinIcons.Components.Default))) _selected = element;
        GUILayout.EndHorizontal();
        foreach (var child in element.Children) DrawHierarchy(child, depth + 1);
    }

    private static void DrawPreview(VisualElement element, int depth)
    {
        GUILayout.BeginHorizontal(); GUILayout.Space(depth * 12);
        var text = element switch { TextElement label => label.text, TextField field => field.value, _ => element.name };
        GUILayout.Box(string.IsNullOrWhiteSpace(text) ? element.GetType().Name : text,
            GUILayout.Height((Fix64)Math.Max(22, element.style.height)));
        GUILayout.EndHorizontal();
        foreach (var child in element.Children) DrawPreview(child, depth + 1);
    }

    private void DrawInspector()
    {
        GUILayout.Label("Inspector", EditorStyles.boldLabel);
        GUI.enabled = false; EditorGUILayout.TextField("Type", _selected.GetType().Name); GUI.enabled = true;
        var name = EditorGUILayout.TextField("Name", _selected.name);
        if (name != _selected.name) { _selected.name = name; _dirty = true; }
        if (_selected is TextElement textElement)
        {
            var text = EditorGUILayout.TextField("Text", textElement.text);
            if (text != textElement.text) { textElement.text = text; _dirty = true; }
        }
        else if (_selected is TextField textField)
        {
            var value = EditorGUILayout.TextField("Value", textField.value);
            if (value != textField.value) { textField.value = value; _dirty = true; }
        }
        var directions = Enum.GetNames<FlexDirection>();
        var direction = EditorGUILayout.Popup("Direction", (int)_selected.style.flexDirection, directions);
        _selected.style.flexDirection = (FlexDirection)Math.Clamp(direction, 0, directions.Length - 1);
        _selected.style.width = EditorGUILayout.FloatField("Width", _selected.style.width);
        _selected.style.height = EditorGUILayout.FloatField("Height", _selected.style.height);
        _selected.style.flexGrow = EditorGUILayout.FloatField("Flex Grow", _selected.style.flexGrow);
        _selected.style.fontSize = EditorGUILayout.FloatField("Font Size", _selected.style.fontSize);
        if (!ReferenceEquals(_selected, _documentRoot) && GUILayout.Button(
                new GUIContent("Delete", EditorBuiltinIcons.Toolbar.Delete, "Delete selected element")))
        {
            var parent = _selected.parent!; _selected.RemoveFromHierarchy(); _selected = parent; _dirty = true;
        }
    }

    private void AddElement(VisualElement element) { _selected.Add(element); _selected = element; _dirty = true; }
    private void NewDocument() { _documentRoot = CreateDefaultDocument(); _selected = _documentRoot; _assetPath = ""; _dirty = true; }
    private void LoadDocument(string path)
    {
        var full = ResolveAssetPath(path); _documentRoot = VisualTreeAsset.Load(full).Instantiate();
        _selected = _documentRoot; _assetPath = ToProjectPath(full); _dirty = false;
    }
    private void SaveDocument()
    {
        if (string.IsNullOrWhiteSpace(_assetPath)) _assetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/UI/New UI.uxml");
        var full = ResolveAssetPath(_assetPath); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        VisualTreeAsset.Create(_documentRoot).Save(full); AssetDatabase.ImportAsset(_assetPath,
            ImportAssetOptions.ForceSynchronousImport); AssetDatabase.Refresh(); _dirty = false;
    }
    private static VisualElement CreateDefaultDocument()
    {
        var root = new VisualElement { name = "Root" }; root.style.flexGrow = 1; root.style.SetPadding(32);
        root.Add(new Label("BEngine UI") { name = "Title" });
        root.Add(new Button(text: "Start") { name = "StartButton" }); return root;
    }
    private static string ResolveAssetPath(string path)
    {
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        var root = Directory.GetParent(Path.GetFullPath(Application.dataPath))?.FullName ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
    }
    private static string ToProjectPath(string path)
    {
        var root = Directory.GetParent(Path.GetFullPath(Application.dataPath))?.FullName ?? Directory.GetCurrentDirectory();
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }
}
