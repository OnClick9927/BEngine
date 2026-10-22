namespace BEngine.Editor;

/// <summary>Edits an engine document copy while preserving the Photoshop/Aseprite source.</summary>
public sealed class ArtDocumentWindow : EditorWindow
{
    private ArtDocument2D? _document;
    private int _selected;
    private Vector2 _scroll;
    private string _status = "Select a layered art document.";
    private string? _previewPath;
    private bool _previewDirty = true;
    public ArtDocumentWindow() { titleContent = new GUIContent("Layered Art"); minSize = new(640, 400); }
    [MenuItem("Window/2D/Layered Art", false, 215)]
    public static void Open() => GetWindow<ArtDocumentWindow>("Layered Art", true).ShowAuxWindow();
    public static void Open(ArtDocument2D document)
    {
        if (document.SourceKind != ArtDocumentSourceKind.Photoshop) throw new NotSupportedException("The layered editor edits Photoshop documents; Aseprite frames retain their imported animation composite.");
        var window = GetWindow<ArtDocumentWindow>("Layered Art", true); window._document = document; window._selected = 0; window._previewDirty = true; window.ShowAuxWindow();
    }
    protected override void OnSelectionChange()
    {
        if (Selection.activeObject is ArtDocument2D { SourceKind: ArtDocumentSourceKind.Photoshop } document) { _document = document; _selected = 0; _previewDirty = true; Repaint(); }
    }
    protected override void OnGUI()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Save Editable Copy", EditorStyles.toolbarButton) && _document is { } source)
        {
            EditorFileDialog.Save("Save layered art", Application.dataPath, "Layered art|*.art2d.yaml", source.name + ".art2d.yaml", path =>
            {
                try { SaveCopy(source, path); _status = "Saved " + path; AssetDatabase.Refresh(); }
                catch (Exception error) { _status = error.Message; }
            });
        }
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
        if (_document is not { } document) { GUILayout.Label(_status); return; }
        GUILayout.Label($"{document.name}: {document.CanvasWidth} × {document.CanvasHeight}", EditorStyles.boldLabel);
        if (_previewDirty && Event.current.type == EventType.Repaint)
        {
            _previewDirty = false;
            try
            {
                var preview = ArtDocumentPreview.Compose(document); var texture = Texture.FromRgba32(preview.Width, preview.Height, preview.Rgba);
                try { _previewPath ??= Path.Combine(Path.GetTempPath(), "bengine-art-" + Guid.NewGuid().ToString("N") + ".png"); File.WriteAllBytes(_previewPath, texture.EncodeToPNG()); }
                finally { BObject.DestroyImmediate(texture); }
            }
            catch (Exception error) { _status = error.Message; }
        }
        var previewRect = GUILayoutUtility.GetControlRect(Fix64.Min(140, GUIUtility.currentViewHeight / 4));
        if (_previewPath is not null)
        {
            var scale = Fix64.Min(previewRect.width / document.CanvasWidth, previewRect.height / document.CanvasHeight);
            GUI.DrawTexture(new Rect(previewRect.x, previewRect.y, document.CanvasWidth * scale, document.CanvasHeight * scale), _previewPath);
        }
        var viewport = GUILayoutUtility.GetControlRect(Fix64.Max(40, GUIUtility.currentViewHeight - 450));
        _scroll = GUI.BeginScrollView(viewport, _scroll, new Rect(0, 0, viewport.width - 16, Fix64.Max(viewport.height, document.Layers.Count * 26)));
        for (var index = 0; index < document.Layers.Count; index++)
        {
            var layer = document.Layers[index]; var depth = 0;
            for (var parent = layer.ParentIndex; parent >= 0 && depth < 64; parent = document.Layers[parent].ParentIndex) depth++;
            if (GUI.Button(new Rect(4 + depth * 14, index * 26, viewport.width - 24 - depth * 14, 24),
                $"{(layer.IsGroup ? "▸ " : "")}{layer.Name} {(layer.Visible ? "" : "(hidden)")}")) _selected = index;
        }
        GUI.EndScrollView();
        if (document.Layers.Count > 0)
        {
            _selected = Math.Clamp(_selected, 0, document.Layers.Count - 1); var layer = document.Layers[_selected];
            var name = EditorGUILayout.TextField("Layer", layer.Name);
            var visible = EditorGUILayout.Toggle("Visible", layer.Visible);
            var opacity = Math.Clamp(EditorGUILayout.IntField("Opacity", layer.Opacity), 0, 255);
            var x = layer.IsGroup ? layer.X : EditorGUILayout.IntField("X", layer.X);
            var y = layer.IsGroup ? layer.Y : EditorGUILayout.IntField("Y", layer.Y);
            var groups = Enumerable.Range(0, document.Layers.Count).Where(index => document.Layers[index].IsGroup && index != _selected).ToArray();
            var choices = new[] { "Root" }.Concat(groups.Select(index => document.Layers[index].Name + " (#" + index + ")")).ToArray();
            var current = Array.IndexOf(groups, layer.ParentIndex) + 1;
            var chosen = EditorGUILayout.Popup("Parent Group", current, choices);
            var parent = chosen == 0 ? -1 : groups[chosen - 1];
            if (name != layer.Name || visible != layer.Visible || opacity != layer.Opacity || x != layer.X || y != layer.Y || parent != layer.ParentIndex)
            {
                try { EditLayer(document, _selected, name, visible, (byte)opacity, x, y, parent); _previewDirty = true; _status = "Edited; save an editable copy to retain changes."; }
                catch (Exception error) { _status = error.Message; }
            }
        }
        GUILayout.Label(_status, EditorStyles.miniLabel);
    }
    protected override void OnEnable() { Undo.undoRedoPerformed += RefreshPreview; }
    protected override void OnDisable()
    {
        Undo.undoRedoPerformed -= RefreshPreview;
        if (_previewPath is not null) { File.Delete(_previewPath); _previewPath = null; }
    }
    private void RefreshPreview() { _previewDirty = true; Repaint(); }
    public static void EditLayer(ArtDocument2D document, int index, string name, bool visible, byte opacity, int x, int y, int parent)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (index < 0 || index >= document.Layers.Count || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Select a layer and provide a name.");
        var visited = new HashSet<int> { index };
        for (var current = parent; current >= 0; current = document.Layers[current].ParentIndex)
            if (current >= document.Layers.Count || !document.Layers[current].IsGroup || !visited.Add(current)) throw new ArgumentException("Choose a parent group outside this layer's descendants.");
        Undo.RecordObject(document, "Edit Art Layer");
        var layer = document.Layers[index]; layer.Name = name.Trim(); layer.Visible = visible; layer.Opacity = opacity; layer.X = x; layer.Y = y; layer.ParentIndex = parent;
        EditorUtility.SetDirty(document);
    }
    public static void SaveCopy(ArtDocument2D document, string path)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Save layered art");
        path = Path.GetFullPath(path); var assets = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(assets, StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".art2d.yaml", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Save an .art2d.yaml document inside Assets.");
        File.WriteAllText(path, document.Serialize());
    }
}
