using BEngine.Documents;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/ProjectSettings.png")]
public sealed class SortingLayersWindow : EditorWindow
{
    private readonly string[] _names = new string[SortingLayer.MaximumIndex];
    private Vector2 _scroll;
    private string _settingsPath = string.Empty;
    private bool _dirty;
    private string _error = string.Empty;

    [MenuItem("Edit/Sorting Layers...", false, 905)]
    [MenuItem("Window/Rendering/Sorting Layers", false, 210)]
    public static void Open() => GetWindow<SortingLayersWindow>("Sorting Layers").Focus();

    protected override void OnEnable()
    {
        titleContent = new GUIContent("Sorting Layers", "Icons/Windows/ProjectSettings.png",
            "Project sorting layers");
        minSize = new Vector2(520, 420);
        Reload();
    }

    protected override void OnProjectChange() => Reload();

    protected override void OnGUI()
    {
        EnsureProject();
        GUILayout.Label("Sorting Layers", EditorStyles.largeLabel);
        GUILayout.Label("World: 2^1 - 2^58    UI: 2^59 - 2^63",
            EditorStyles.miniLabel);
        var viewportHeight = Fix64.Max(120, GUIUtility.currentViewHeight - 116);
        var viewport = GUILayoutUtility.GetControlRect(viewportHeight);
        var rowHeight = Fix64.Max(22, EditorGUIUtility.singleLineHeight + 4);
        var contentWidth = Fix64.Max(260, viewport.width - 14);
        _scroll = GUI.BeginScrollView(viewport, _scroll,
            new Rect(0, 0, contentWidth, rowHeight * SortingLayer.MaximumIndex + 6));
        try
        {
            for (var index = SortingLayer.MinimumIndex; index <= SortingLayer.MaximumIndex; index++)
            {
                var ui = index >= SortingLayer.UiBaseIndex;
                var y = 3 + (index - SortingLayer.MinimumIndex) * rowHeight;
                GUI.Label(new Rect(4, y, 54, rowHeight), $"2^{index}");
                GUI.Label(new Rect(60, y, 48, rowHeight), ui ? "UI" : "World");
                var value = GUI.TextField(new Rect(112, y, Fix64.Max(120, contentWidth - 116),
                    rowHeight - 2), _names[index - SortingLayer.MinimumIndex]);
                if (!value.Equals(_names[index - SortingLayer.MinimumIndex], StringComparison.Ordinal))
                {
                    _names[index - SortingLayer.MinimumIndex] = value;
                    _dirty = true;
                    Validate();
                }
            }
        }
        finally { GUI.EndScrollView(); }

        if (_error.Length > 0) EditorGUILayout.HelpBox(_error, MessageType.Error);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUI.BeginDisabledGroup(!_dirty);
        if (GUILayout.Button("Revert", GUILayout.Width(84))) Reload();
        EditorGUI.EndDisabledGroup();
        EditorGUI.BeginDisabledGroup(!_dirty || _error.Length > 0);
        if (GUILayout.Button("Apply", GUILayout.Width(84))) Save();
        EditorGUI.EndDisabledGroup();
        GUILayout.EndHorizontal();
    }

    private void EnsureProject()
    {
        if (_settingsPath.Equals(EditorProjectSettings.settingsPath, StringComparison.OrdinalIgnoreCase)) return;
        Reload();
    }

    private void Reload()
    {
        _settingsPath = EditorProjectSettings.settingsPath;
        var layers = EditorProjectSettings.current.SortingLayers;
        var names = layers.ToDictionary(item => item.Value, item => item.Name);
        foreach (var definition in SortingLayerRegistry.CreateDefaults())
            _names[definition.Index - SortingLayer.MinimumIndex] =
                names.GetValueOrDefault(definition.Value, definition.Name);
        _dirty = false;
        _error = string.Empty;
    }

    private void Validate()
    {
        if (_names.Any(string.IsNullOrWhiteSpace))
        {
            _error = "Every sorting layer needs a name.";
            return;
        }
        var duplicate = _names.GroupBy(name => name.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        _error = duplicate is null ? string.Empty : $"Layer name '{duplicate.Key}' is duplicated.";
    }

    private void Save()
    {
        Validate();
        if (_error.Length > 0) return;
        EditorProjectSettings.current.SortingLayers = Enumerable.Range(
                SortingLayer.MinimumIndex, SortingLayer.MaximumIndex)
            .Select(index => new SortingLayerDocument
            {
                Value = SortingLayer.FromIndex(index),
                Name = _names[index - SortingLayer.MinimumIndex].Trim()
            }).ToList();
        EditorProjectSettings.Save();
        _dirty = false;
    }
}
