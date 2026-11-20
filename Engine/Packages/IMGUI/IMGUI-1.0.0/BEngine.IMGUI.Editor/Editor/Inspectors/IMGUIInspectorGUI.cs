namespace BEngine.Editor;

internal sealed class IMGUIInspectorGUI : IEditorInspectorGUI
{
    internal static readonly IMGUIInspectorGUI Instance = new();
    private IMGUIInspectorGUI() { }
    public void Heading(string text) => GUILayout.Label(text, EditorStyles.boldLabel);
    public void Space(Fix64 pixels) => GUILayout.Space(pixels);
    public void ReadOnlyText(string label, string value)
    {
        using var disabled = new EditorGUI.DisabledScope(true);
        EditorGUILayout.TextField(label, value ?? string.Empty);
    }
    public void PropertyField(SerializedProperty property, bool includeChildren) =>
        EditorGUILayout.PropertyField(property, includeChildren: includeChildren);
    public void BeginChangeCheck() => EditorGUI.BeginChangeCheck();
    public bool EndChangeCheck() => EditorGUI.EndChangeCheck();
    public int IntField(string label, int value) => EditorGUILayout.IntField(label, value);
    public bool Toggle(string label, bool value) => EditorGUILayout.Toggle(label, value);
    public Enum EnumPopup(string label, Enum value) => EditorGUILayout.EnumPopup(label, value);
}
