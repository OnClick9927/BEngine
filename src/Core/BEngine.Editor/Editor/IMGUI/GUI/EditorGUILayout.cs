using System.Globalization;

namespace BEngine.Editor;

public static class EditorGUILayout
{
    public static bool PropertyField(SerializedProperty property, GUIContent? label = null,
        bool includeChildren = false, params GUILayoutOption[] options)
    {
        var height = EditorGUI.GetPropertyHeight(property, label, includeChildren);
        return EditorGUI.PropertyField(GUILayoutUtility.GetControlRect(height, options), property, label,
            includeChildren);
    }
    public static void LabelField(string label, params GUILayoutOption[] options) => GUILayout.Label(label, options);
    public static void LabelField(string label, GUIStyle style, params GUILayoutOption[] options) =>
        GUILayout.Label(label, style, options);
    public static void LabelField(GUIContent label, GUIStyle? style = null, params GUILayoutOption[] options) =>
        GUILayout.Label(label, style ?? EditorStyles.label, options);
    public static string TextField(string label, string value, params GUILayoutOption[] options) =>
        EditorGUI.TextField(GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight, options), label, value);
    public static string TextArea(string value, params GUILayoutOption[] options) =>
        GUI.TextArea(GUILayoutUtility.GetControlRect(100, options), value);
    public static int IntField(string label, int value, params GUILayoutOption[] options) =>
        EditorGUI.IntField(GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight, options), label, value);
    public static float FloatField(string label, float value, params GUILayoutOption[] options) =>
        EditorGUI.FloatField(GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight, options), label, value);
    public static bool Toggle(string label, bool value, params GUILayoutOption[] options) =>
        EditorGUI.Toggle(GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight, options), label, value);
    public static Color ColorField(string label, Color value, params GUILayoutOption[] options) =>
        ColorField(label, value, true, true, false, options);
    public static Color ColorField(string label, Color value, bool showEyedropper, bool showAlpha, bool hdr,
        params GUILayoutOption[] options) => EditorGUI.ColorField(
        GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight, options), label, value,
        showEyedropper, showAlpha, hdr);
    public static Color ColorField(GUIContent label, Color value, bool showEyedropper = true,
        bool showAlpha = true, bool hdr = false, params GUILayoutOption[] options) => EditorGUI.ColorField(
        GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight, options), label, value,
        showEyedropper, showAlpha, hdr);
    public static Color ColorField(Color value, bool showEyedropper = true, bool showAlpha = true,
        bool hdr = false, params GUILayoutOption[] options) => EditorGUI.ColorField(
        GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight, options), value,
        showEyedropper, showAlpha, hdr);
    public static int Popup(string label, int selectedIndex, string[] displayedOptions,
        params GUILayoutOption[] options) => EditorGUI.Popup(
        GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight, options), label, selectedIndex,
        displayedOptions);
    public static Enum EnumPopup(string label, Enum selected, params GUILayoutOption[] options) =>
        EditorGUI.EnumPopup(GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight, options),
            label, selected);
    public static float Slider(string label, float value, float leftValue, float rightValue,
        params GUILayoutOption[] options) => EditorGUI.Slider(
        GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight, options), label, value,
        leftValue, rightValue);
    public static int IntSlider(string label, int value, int leftValue, int rightValue,
        params GUILayoutOption[] options) => EditorGUI.IntSlider(
        GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight, options), label, value,
        leftValue, rightValue);
    public static Vector2 Vector2Field(string label, Vector2 value, params GUILayoutOption[] options) =>
        EditorGUI.Vector2Field(GUILayoutUtility.GetControlRect(EditorGUI.GetVectorFieldHeight(2), options), label, value);
    public static Vector4 Vector4Field(string label, Vector4 value, params GUILayoutOption[] options) =>
        EditorGUI.Vector4Field(GUILayoutUtility.GetControlRect(EditorGUI.GetVectorFieldHeight(4), options), label, value);
    public static bool Foldout(bool foldout, string content, params GUILayoutOption[] options) =>
        EditorGUI.Foldout(GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight, options), foldout, content);
    public static void HelpBox(string message, MessageType type = MessageType.Info) =>
        EditorGUI.HelpBox(GUILayoutUtility.GetControlRect(38), message, type);
}
