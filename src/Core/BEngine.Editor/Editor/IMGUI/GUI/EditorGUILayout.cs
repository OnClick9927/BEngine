namespace BEngine.Editor;

public static class EditorGUILayout
{
    public static bool PropertyField(SerializedProperty property, GUIContent? label = null,
        bool includeChildren = false, params GUILayoutOption[] options) =>
        PropertyField(property, label, includeChildren, null, options);

    public static bool PropertyField(SerializedProperty property, GUIStyle? style,
        params GUILayoutOption[] options) => PropertyField(property, null, false, style, options);

    public static bool PropertyField(SerializedProperty property, GUIContent? label, GUIStyle? style,
        params GUILayoutOption[] options) => PropertyField(property, label, false, style, options);

    public static bool PropertyField(SerializedProperty property, GUIContent? label,
        bool includeChildren, GUIStyle? style, params GUILayoutOption[] options)
    {
        var height = EditorGUI.GetPropertyHeight(property, label, includeChildren);
        if (style is not null && style.fixedHeight > 0) height = Fix64.Max(height, style.fixedHeight);
        return EditorGUI.PropertyField(GUILayoutUtility.GetControlRect(height, options), property, label,
            includeChildren, style);
    }

    public static void LabelField(string label, params GUILayoutOption[] options) =>
        LabelField(new GUIContent(label), null, options);

    public static void LabelField(string label, GUIStyle? style, params GUILayoutOption[] options) =>
        LabelField(new GUIContent(label), style, options);

    public static void LabelField(GUIContent label, GUIStyle? style = null,
        params GUILayoutOption[] options) => EditorGUI.LabelField(
        GUILayoutUtility.GetControlRect(StyleHeight(style, EditorStyles.label,
            GUILayout.DefaultControlHeight), options), label, style);

    public static string TextField(string label, string value, params GUILayoutOption[] options) =>
        TextField(label, value, null, options);

    public static string TextField(string label, string value, GUIStyle? style,
        params GUILayoutOption[] options) => EditorGUI.TextField(
        GUILayoutUtility.GetControlRect(StyleHeight(style, EditorStyles.textField,
            EditorGUIUtility.singleLineHeight), options), label, value, style);

    public static string TextArea(string value, params GUILayoutOption[] options) =>
        TextArea(value, null, options);

    public static string TextArea(string value, GUIStyle? style, params GUILayoutOption[] options) =>
        EditorGUI.TextArea(GUILayoutUtility.GetControlRect(
            StyleHeight(style, EditorStyles.textArea, 100), options), value, style);

    public static int IntField(string label, int value, params GUILayoutOption[] options) =>
        IntField(label, value, null, options);

    public static int IntField(string label, int value, GUIStyle? style,
        params GUILayoutOption[] options) => EditorGUI.IntField(
        GUILayoutUtility.GetControlRect(StyleHeight(style, EditorStyles.numberField,
            EditorGUIUtility.singleLineHeight), options), label, value, style);

    public static float FloatField(string label, float value, params GUILayoutOption[] options) =>
        FloatField(label, value, null, options);

    public static float FloatField(string label, float value, GUIStyle? style,
        params GUILayoutOption[] options) => EditorGUI.FloatField(
        GUILayoutUtility.GetControlRect(StyleHeight(style, EditorStyles.numberField,
            EditorGUIUtility.singleLineHeight), options), label, value, style);

    public static bool Toggle(string label, bool value, params GUILayoutOption[] options) =>
        Toggle(label, value, null, options);

    public static bool Toggle(string label, bool value, GUIStyle? style,
        params GUILayoutOption[] options) => EditorGUI.Toggle(
        GUILayoutUtility.GetControlRect(StyleHeight(style, GUI.skin.toggle,
            EditorGUIUtility.singleLineHeight), options), label, value, style);

    public static Color ColorField(string label, Color value, params GUILayoutOption[] options) =>
        ColorField(label, value, true, true, false, null, options);

    public static Color ColorField(string label, Color value, GUIStyle? style,
        params GUILayoutOption[] options) => ColorField(label, value, true, true, false, style, options);

    public static Color ColorField(string label, Color value, bool showEyedropper, bool showAlpha, bool hdr,
        params GUILayoutOption[] options) =>
        ColorField(label, value, showEyedropper, showAlpha, hdr, null, options);

    public static Color ColorField(string label, Color value, bool showEyedropper, bool showAlpha, bool hdr,
        GUIStyle? style, params GUILayoutOption[] options) => EditorGUI.ColorField(
        GUILayoutUtility.GetControlRect(StyleHeight(style, EditorStyles.colorField,
            EditorGUIUtility.singleLineHeight), options), label, value, showEyedropper, showAlpha, hdr, style);

    public static Color ColorField(GUIContent label, Color value, bool showEyedropper = true,
        bool showAlpha = true, bool hdr = false, params GUILayoutOption[] options) =>
        ColorField(label, value, showEyedropper, showAlpha, hdr, null, options);

    public static Color ColorField(GUIContent label, Color value, GUIStyle? style,
        params GUILayoutOption[] options) => ColorField(label, value, true, true, false, style, options);

    public static Color ColorField(GUIContent label, Color value, bool showEyedropper, bool showAlpha,
        bool hdr, GUIStyle? style, params GUILayoutOption[] options) => EditorGUI.ColorField(
        GUILayoutUtility.GetControlRect(StyleHeight(style, EditorStyles.colorField,
            EditorGUIUtility.singleLineHeight), options), label, value, showEyedropper, showAlpha, hdr, style);

    public static Color ColorField(Color value, bool showEyedropper = true, bool showAlpha = true,
        bool hdr = false, params GUILayoutOption[] options) =>
        ColorField(value, showEyedropper, showAlpha, hdr, null, options);

    public static Color ColorField(Color value, GUIStyle? style,
        params GUILayoutOption[] options) => ColorField(value, true, true, false, style, options);

    public static Color ColorField(Color value, bool showEyedropper, bool showAlpha, bool hdr,
        GUIStyle? style, params GUILayoutOption[] options) => EditorGUI.ColorField(
        GUILayoutUtility.GetControlRect(StyleHeight(style, EditorStyles.colorField,
            EditorGUIUtility.singleLineHeight), options), value, showEyedropper, showAlpha, hdr, style);

    public static BObject? ObjectField(BObject? value, Type objectType, bool allowSceneObjects,
        params GUILayoutOption[] options) => ObjectField(value, objectType, allowSceneObjects, null, options);

    public static BObject? ObjectField(BObject? value, Type objectType, bool allowSceneObjects,
        GUIStyle? style, params GUILayoutOption[] options) => EditorGUI.ObjectField(
        ObjectFieldRect(style, options), value, objectType, allowSceneObjects, style);

    public static BObject? ObjectField(string label, BObject? value, Type objectType,
        bool allowSceneObjects, params GUILayoutOption[] options) =>
        ObjectField(label, value, objectType, allowSceneObjects, null, options);

    public static BObject? ObjectField(string label, BObject? value, Type objectType,
        bool allowSceneObjects, GUIStyle? style, params GUILayoutOption[] options) => EditorGUI.ObjectField(
        ObjectFieldRect(style, options), label, value, objectType, allowSceneObjects, style);

    public static BObject? ObjectField(GUIContent label, BObject? value, Type objectType,
        bool allowSceneObjects, params GUILayoutOption[] options) =>
        ObjectField(label, value, objectType, allowSceneObjects, null, options);

    public static BObject? ObjectField(GUIContent label, BObject? value, Type objectType,
        bool allowSceneObjects, GUIStyle? style, params GUILayoutOption[] options) => EditorGUI.ObjectField(
        ObjectFieldRect(style, options), label, value, objectType, allowSceneObjects, style);

    public static T? ObjectField<T>(T? value, bool allowSceneObjects, params GUILayoutOption[] options)
        where T : BObject => ObjectField(value, allowSceneObjects, null, options);

    public static T? ObjectField<T>(T? value, bool allowSceneObjects, GUIStyle? style,
        params GUILayoutOption[] options) where T : BObject => EditorGUI.ObjectField(
        ObjectFieldRect(style, options), value, allowSceneObjects, style);

    public static T? ObjectField<T>(string label, T? value, bool allowSceneObjects,
        params GUILayoutOption[] options) where T : BObject =>
        ObjectField(label, value, allowSceneObjects, null, options);

    public static T? ObjectField<T>(string label, T? value, bool allowSceneObjects, GUIStyle? style,
        params GUILayoutOption[] options) where T : BObject => EditorGUI.ObjectField(
        ObjectFieldRect(style, options), label, value, allowSceneObjects, style);

    public static T? ObjectField<T>(GUIContent label, T? value, bool allowSceneObjects,
        params GUILayoutOption[] options) where T : BObject =>
        ObjectField(label, value, allowSceneObjects, null, options);

    public static T? ObjectField<T>(GUIContent label, T? value, bool allowSceneObjects, GUIStyle? style,
        params GUILayoutOption[] options) where T : BObject => EditorGUI.ObjectField(
        ObjectFieldRect(style, options), label, value, allowSceneObjects, style);

    public static void ObjectField(SerializedProperty property, Type objectType,
        params GUILayoutOption[] options) => ObjectField(property, objectType, (GUIStyle?)null, options);

    public static void ObjectField(SerializedProperty property, Type objectType, GUIStyle? style,
        params GUILayoutOption[] options) => EditorGUI.ObjectField(
        ObjectFieldRect(style, options), property, objectType, style);

    public static void ObjectField(SerializedProperty property, Type objectType, bool allowSceneObjects,
        params GUILayoutOption[] options) =>
        ObjectField(property, objectType, allowSceneObjects, null, options);

    public static void ObjectField(SerializedProperty property, Type objectType, bool allowSceneObjects,
        GUIStyle? style, params GUILayoutOption[] options) => EditorGUI.ObjectField(
        ObjectFieldRect(style, options), property, objectType, allowSceneObjects, style);

    public static void ObjectField(SerializedProperty property, Type objectType, GUIContent label,
        params GUILayoutOption[] options) => ObjectField(property, objectType, label, null, options);

    public static void ObjectField(SerializedProperty property, Type objectType, GUIContent label,
        GUIStyle? style, params GUILayoutOption[] options) => EditorGUI.ObjectField(
        ObjectFieldRect(style, options), property, objectType, label, style);

    public static void ObjectField(SerializedProperty property, Type objectType, string label,
        params GUILayoutOption[] options) => ObjectField(property, objectType, label, null, options);

    public static void ObjectField(SerializedProperty property, Type objectType, string label,
        GUIStyle? style, params GUILayoutOption[] options) => EditorGUI.ObjectField(
        ObjectFieldRect(style, options), property, objectType, label, style);

    public static void ObjectField(SerializedProperty property, Type objectType, GUIContent label,
        bool allowSceneObjects, params GUILayoutOption[] options) =>
        ObjectField(property, objectType, label, allowSceneObjects, null, options);

    public static void ObjectField(SerializedProperty property, Type objectType, GUIContent label,
        bool allowSceneObjects, GUIStyle? style, params GUILayoutOption[] options) => EditorGUI.ObjectField(
        ObjectFieldRect(style, options), property, objectType, label, allowSceneObjects, style);

    public static void ObjectField(SerializedProperty property, Type objectType, string label,
        bool allowSceneObjects, params GUILayoutOption[] options) =>
        ObjectField(property, objectType, label, allowSceneObjects, null, options);

    public static void ObjectField(SerializedProperty property, Type objectType, string label,
        bool allowSceneObjects, GUIStyle? style, params GUILayoutOption[] options) => EditorGUI.ObjectField(
        ObjectFieldRect(style, options), property, objectType, label, allowSceneObjects, style);

    public static int Popup(string label, int selectedIndex, string[] displayedOptions,
        params GUILayoutOption[] options) => Popup(label, selectedIndex, displayedOptions, null, options);

    public static int Popup(string label, int selectedIndex, string[] displayedOptions, GUIStyle? style,
        params GUILayoutOption[] options) => EditorGUI.Popup(
        GUILayoutUtility.GetControlRect(StyleHeight(style, EditorStyles.popup,
            EditorGUIUtility.singleLineHeight), options), label, selectedIndex, displayedOptions, style);

    public static int AdvancedPopup(string label, int selectedIndex, string[] displayedOptions,
        params GUILayoutOption[] options) => AdvancedPopup(label, selectedIndex, displayedOptions, null, options);

    public static int AdvancedPopup(string label, int selectedIndex, string[] displayedOptions,
        GUIStyle? style, params GUILayoutOption[] options) => EditorGUI.AdvancedPopup(
        GUILayoutUtility.GetControlRect(StyleHeight(style, EditorStyles.popup,
            EditorGUIUtility.singleLineHeight), options), label, selectedIndex, displayedOptions, style);

    public static bool DropDownButton(string text, FocusType focusType,
        params GUILayoutOption[] options) => DropDownButton(text, focusType, null, options);

    public static bool DropDownButton(string text, params GUILayoutOption[] options) =>
        DropDownButton(text, FocusType.Keyboard, null, options);

    public static bool DropDownButton(string text, GUIStyle? style, params GUILayoutOption[] options) =>
        DropDownButton(text, FocusType.Keyboard, style, options);

    public static bool DropDownButton(GUIContent content, FocusType focusType,
        params GUILayoutOption[] options) => DropDownButton(content, focusType, null, options);

    public static bool DropDownButton(GUIContent content, params GUILayoutOption[] options) =>
        DropDownButton(content, FocusType.Keyboard, null, options);

    public static bool DropDownButton(GUIContent content, GUIStyle? style,
        params GUILayoutOption[] options) => DropDownButton(content, FocusType.Keyboard, style, options);

    public static bool DropDownButton(string text, FocusType focusType, GUIStyle? style,
        params GUILayoutOption[] options) => EditorGUI.DropDownButton(
        GUILayoutUtility.GetControlRect(StyleHeight(style, EditorStyles.dropDownButton,
            EditorGUIUtility.singleLineHeight), options), text, focusType, style);

    public static bool DropDownButton(GUIContent content, FocusType focusType, GUIStyle? style,
        params GUILayoutOption[] options) => EditorGUI.DropDownButton(
        GUILayoutUtility.GetControlRect(StyleHeight(style, EditorStyles.dropDownButton,
            EditorGUIUtility.singleLineHeight), options), content, focusType, style);

    public static bool DropdownButton(string text, FocusType focusType,
        params GUILayoutOption[] options) => DropDownButton(text, focusType, null, options);

    public static bool DropdownButton(GUIContent content, FocusType focusType,
        params GUILayoutOption[] options) => DropDownButton(content, focusType, null, options);

    public static bool DropdownButton(string text, FocusType focusType, GUIStyle? style,
        params GUILayoutOption[] options) => DropDownButton(text, focusType, style, options);

    public static bool DropdownButton(GUIContent content, FocusType focusType, GUIStyle? style,
        params GUILayoutOption[] options) => DropDownButton(content, focusType, style, options);

    public static bool DropdownButton(string text, params GUILayoutOption[] options) =>
        DropDownButton(text, FocusType.Keyboard, null, options);

    public static bool DropdownButton(GUIContent content, params GUILayoutOption[] options) =>
        DropDownButton(content, FocusType.Keyboard, null, options);

    public static bool DropdownButton(string text, GUIStyle? style, params GUILayoutOption[] options) =>
        DropDownButton(text, FocusType.Keyboard, style, options);

    public static bool DropdownButton(GUIContent content, GUIStyle? style,
        params GUILayoutOption[] options) => DropDownButton(content, FocusType.Keyboard, style, options);

    public static Enum EnumPopup(string label, Enum selected, params GUILayoutOption[] options) =>
        EnumPopup(label, selected, null, options);

    public static Enum EnumPopup(string label, Enum selected, GUIStyle? style,
        params GUILayoutOption[] options) => EditorGUI.EnumPopup(
        GUILayoutUtility.GetControlRect(StyleHeight(style, EditorStyles.popup,
            EditorGUIUtility.singleLineHeight), options), label, selected, style);

    public static float Slider(string label, float value, float leftValue, float rightValue,
        params GUILayoutOption[] options) => Slider(label, value, leftValue, rightValue, null, options);

    public static float Slider(string label, float value, float leftValue, float rightValue,
        GUIStyle? style, params GUILayoutOption[] options) => EditorGUI.Slider(
        GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight, options), label, value,
        leftValue, rightValue, style);

    public static int IntSlider(string label, int value, int leftValue, int rightValue,
        params GUILayoutOption[] options) => IntSlider(label, value, leftValue, rightValue, null, options);

    public static int IntSlider(string label, int value, int leftValue, int rightValue,
        GUIStyle? style, params GUILayoutOption[] options) => EditorGUI.IntSlider(
        GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight, options), label, value,
        leftValue, rightValue, style);

    public static Vector2 Vector2Field(string label, Vector2 value, params GUILayoutOption[] options) =>
        Vector2Field(label, value, null, options);

    public static Vector2 Vector2Field(string label, Vector2 value, GUIStyle? style,
        params GUILayoutOption[] options) => EditorGUI.Vector2Field(
        GUILayoutUtility.GetControlRect(EditorGUI.GetVectorFieldHeight(2), options), label, value, style);

    public static Vector4 Vector4Field(string label, Vector4 value, params GUILayoutOption[] options) =>
        Vector4Field(label, value, null, options);

    public static Vector4 Vector4Field(string label, Vector4 value, GUIStyle? style,
        params GUILayoutOption[] options) => EditorGUI.Vector4Field(
        GUILayoutUtility.GetControlRect(EditorGUI.GetVectorFieldHeight(4), options), label, value, style);

    public static bool Foldout(bool foldout, string content, params GUILayoutOption[] options) =>
        Foldout(foldout, content, null, options);

    public static bool Foldout(bool foldout, string content, GUIStyle? style,
        params GUILayoutOption[] options) => EditorGUI.Foldout(
        GUILayoutUtility.GetControlRect(StyleHeight(style, EditorStyles.foldout,
            EditorGUIUtility.singleLineHeight), options), foldout, content, style);

    public static void HelpBox(string message, MessageType type = MessageType.Info) =>
        HelpBox(message, type, null);

    public static void HelpBox(string message, GUIStyle? style, params GUILayoutOption[] options) =>
        HelpBox(message, MessageType.Info, style, options);

    public static void HelpBox(string message, MessageType type, GUIStyle? style,
        params GUILayoutOption[] options) => EditorGUI.HelpBox(
        GUILayoutUtility.GetControlRect(StyleHeight(style, EditorStyles.helpBox, 38), options),
        message, type, style);

    private static Rect ObjectFieldRect(GUIStyle? style, GUILayoutOption[] options) =>
        GUILayoutUtility.GetControlRect(StyleHeight(style, EditorStyles.popup,
            EditorGUIUtility.singleLineHeight), options);

    private static Fix64 StyleHeight(GUIStyle? style, GUIStyle fallback, Fix64 defaultHeight)
    {
        var resolved = style ?? fallback;
        return resolved.fixedHeight > 0 ? resolved.fixedHeight : defaultHeight;
    }
}
