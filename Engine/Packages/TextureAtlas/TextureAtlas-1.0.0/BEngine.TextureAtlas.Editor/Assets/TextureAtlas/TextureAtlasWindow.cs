using BEngine.Platform;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/Assets/AssetAtlas.png")]
public sealed class TextureAtlasWindow :
    EditorWindow<TextureAtlasWindowModel, TextureAtlasWindowController>
{
    private Vector2 _sourceScroll;

    protected override TextureAtlasWindowModel CreateWindowModel(
        EditorWindowMvcContext context) => new(context.WindowId);

    protected override TextureAtlasWindowController CreateWindowController(
        EditorWindowMvcContext context,
        TextureAtlasWindowModel windowModel) => new(context, windowModel);

    public TextureAtlasWindow()
    {
        titleContent = new GUIContent("Texture Atlas", "Icons/Assets/AssetAtlas.png");
        minSize = new Vector2(940, 560);
        position = new Rect(120, 90, 1080, 700);
    }

    [MenuItem("Window/2D/Texture Atlas", false, 210)]
    public static void Open() => GetWindow<TextureAtlasWindow>("Texture Atlas", true).ShowAuxWindow();

    protected override void OnSelectionChange()
    {
        controller.HandleSelection(Selection.activeObject);
        Repaint();
    }

    protected override void OnGUI()
    {
        DrawToolbar();
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(620));
        DrawSettings();
        DrawSources();
        GUILayout.EndVertical();
        GUILayout.BeginVertical();
        DrawPreview();
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label(model.StatusText, EditorStyles.miniLabel);
    }

    private void DrawToolbar()
    {
        GUILayout.BeginHorizontal();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.New, "New texture atlas", GUILayout.Width(26)))
            controller.NewAtlas();
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
            if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Save,
                    "Save texture atlas", GUILayout.Width(26)))
                controller.SaveAtlas();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Refresh,
                "Reload texture atlas", GUILayout.Width(26)))
            controller.LoadAtlas();
        var assetLocation = EditorGUILayout.TextField("Atlas", model.AssetPath);
        if (!assetLocation.Equals(model.AssetPath, StringComparison.Ordinal))
            controller.SetAssetPath(assetLocation);
        if (GUILayout.Button("Load", GUILayout.Width(60))) controller.LoadAtlas();
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
            if (GUILayout.Button("Build", GUILayout.Width(64))) controller.BuildAtlas();
        GUILayout.EndHorizontal();
    }

    private void DrawSettings()
    {
        GUILayout.Label("Packing", EditorStyles.boldLabel);
        var maxSize = PreviousPowerOfTwo(Math.Clamp(
            EditorGUILayout.IntField("Max Size", model.Atlas.MaxSize), 32, 16384));
        var padding = Math.Clamp(EditorGUILayout.IntField("Padding", model.Atlas.Padding), 0, 64);
        var extrude = Math.Clamp(EditorGUILayout.IntField("Extrude", model.Atlas.Extrude), 0, padding);
        controller.SetPacking(maxSize, padding, extrude);
        GUILayout.Label(model.Atlas.Sprites.Count == 0
            ? "Not built"
            : $"Output: {model.Atlas.Width} x {model.Atlas.Height} | {model.Atlas.Sprites.Count} sprites",
            EditorStyles.miniLabel);
    }

    private void DrawSources()
    {
        GUILayout.Space(8);
        GUILayout.Label("Sprites", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        var pendingSource = EditorGUILayout.ObjectField("Sprite", model.PendingSource,
            typeof(Sprite), false) as Sprite;
        if (!ReferenceEquals(pendingSource, model.PendingSource))
            controller.SetPendingSource(pendingSource);
        using (new EditorGUI.DisabledScope(model.PendingSource is null))
            if (GUILayout.Button("Add", GUILayout.Width(52)))
                controller.AddPendingSource();
        if (GUILayout.Button("Add Selected", GUILayout.Width(96))) controller.AddSelected();
        GUILayout.EndHorizontal();

        var viewportHeight = Fix64.Max(220, GUIUtility.currentViewHeight - 255);
        var viewport = GUILayoutUtility.GetControlRect(viewportHeight);
        var rowHeight = (Fix64)27;
        var contentWidth = Fix64.Max(590, viewport.width - 14);
        var contentHeight = Fix64.Max(viewportHeight, (model.Atlas.Sources.Length + 1) * rowHeight + 4);
        _sourceScroll = GUI.BeginScrollView(viewport, _sourceScroll,
            new Rect(0, 0, contentWidth, contentHeight));
        GUI.Label(new Rect(4, 2, contentWidth - 34, rowHeight - 2),
            "Sprite Asset", EditorStyles.miniLabel);

        var removeIndex = -1;
        for (var index = 0; index < model.Atlas.Sources.Length; index++)
        {
            var source = model.Atlas.Sources[index];
            var y = (index + 1) * rowHeight;
            var selected = EditorGUI.ObjectField(
                new Rect(4, y, contentWidth - 34, rowHeight - 3),
                source, typeof(Sprite), false) as Sprite;
            if (selected is not null && !ReferenceEquals(selected, source))
                controller.ReplaceSource(index, selected);
            if (GUI.Button(new Rect(contentWidth - 24, y, 22, rowHeight - 3),
                    new GUIContent(EditorBuiltinIcons.Toolbar.Delete, tooltip: $"Remove {source.name}"),
                    EditorStyles.toolbarIconButton))
                removeIndex = index;
        }
        GUI.EndScrollView();

        if (removeIndex >= 0) controller.RemoveSource(removeIndex);
    }

    private void DrawPreview()
    {
        GUILayout.Label("Atlas Preview", EditorStyles.boldLabel);
        var available = Fix64.Max(180, GUIUtility.currentViewHeight - 170);
        var rect = GUILayoutUtility.GetControlRect(available);
        if (!string.IsNullOrWhiteSpace(model.Atlas.Texture))
        {
            var atlasWidth = Math.Max(1, model.Atlas.Width);
            var atlasHeight = Math.Max(1, model.Atlas.Height);
            var scale = Fix64.Min(rect.width / atlasWidth, rect.height / atlasHeight);
            var previewWidth = atlasWidth * scale;
            var previewHeight = atlasHeight * scale;
            var preview = new Rect(rect.x + (rect.width - previewWidth) * Fix64.Half,
                rect.y + (rect.height - previewHeight) * Fix64.Half, previewWidth, previewHeight);
            GUI.DrawTexture(preview, TextureAtlasWindowController.ResolvePreviewPath(model.Atlas.Texture));
        }
        else
        {
            GUI.Box(rect, new GUIContent("No generated atlas"), EditorStyles.helpBox);
        }
    }

    private static int PreviousPowerOfTwo(int value)
    {
        var result = 1;
        while (result <= value / 2) result *= 2;
        return Math.Max(32, result);
    }

}
