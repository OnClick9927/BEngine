using System.Reflection;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/Assets/AssetStyle.png")]
public sealed class EditorStylesWindow : EditorBuiltinBrowserWindow
{
    private static readonly PropertyInfo[] Styles = typeof(GUISkin).GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.PropertyType == typeof(GUIStyle)).OrderBy(property => property.Name).ToArray();

    [MenuItem("Tools/EditorStyles", false, 280)]
    public static void Open() => GetWindow<EditorStylesWindow>("Editor Styles");

    protected override void OnEnable()
    {
        minSize = new Vector2(400, 300);
        titleContent = new GUIContent("Editor Styles", EditorBuiltinIcons.Assets.Style);
    }
    protected override void OnGUI()
    {
        DrawSearch("Search built-in styles");
        var matches = Styles.Where(property => property.Name.Contains(model.Search, StringComparison.OrdinalIgnoreCase)).ToArray();
        GUILayout.Label($"{matches.Length} styles · click a name to copy its API", EditorStyles.miniLabel);
        model.Scroll = _scroll.Begin(model.Scroll);
        foreach (var property in matches)
        {
            var style = (GUIStyle)property.GetValue(GUI.skin)!;
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(108));
            var api = (typeof(EditorStyles).GetProperty(property.Name) is null ? "GUI.skin." : "EditorStyles.") + property.Name;
            if (GUILayout.Button(new GUIContent(property.Name, tooltip: "Copy " + api), EditorStyles.boldLabel))
                GUIUtility.systemCopyBuffer = api;
            // A copy allows previews to fit the gallery without modifying the shared skin.
            var preview = new GUIStyle(style) { fixedWidth = 0, fixedHeight = 0 };
            var rect = GUILayoutUtility.GetRect(GUIContent.none, preview, GUILayout.Height(38), GUILayout.ExpandWidth(true));
            var half = Fix64.Max(1, rect.width / 2);
            GUI.Button(new Rect(rect.x, rect.y, half, rect.height), new GUIContent("Normal preview"), preview);
            GUI.Toggle(new Rect(rect.x + half, rect.y, half, rect.height), true, new GUIContent("Selected preview"), preview);
            GUILayout.Label($"Font {style.fontSize}   Size {style.fixedWidth} × {style.fixedHeight}   Wrap {style.wordWrap}", EditorStyles.miniLabel);
            GUILayout.EndVertical();
        }
        _scroll.End();
    }
}
