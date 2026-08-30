using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEditorInternal;

namespace BEngine.Editor;

public static class EditorGUI
{
    private const int NumericDecimals = 4;
    private const int MaxNestedPropertyDepth = 8;
    private const int PropertyIndentWidth = 15;
    private static readonly Fix64 VectorAxisLabelMinimumWidth = 20;
    private static readonly Fix64 VectorNumericMinimumWidth = 66;
    private static readonly Fix64 VectorAxisSpacing = 1;
    private static readonly Fix64 VectorLabelMinimumWidth = 72;
    private static readonly Fix64 VectorCompactLabelWidth = 8;
    private static readonly string[] VectorAxisNames = ["X", "Y", "Z", "W"];
    private static readonly Stack<bool> EnabledStack = new();
    private static readonly Stack<bool> ChangedStack = new();
    private static readonly Dictionary<int, int> PendingPopupSelections = [];
    [ThreadStatic] private static Dictionary<ObjectFieldStructuralIdentity, int>? ObjectFieldOccurrences;
    private static readonly ConditionalWeakTable<SerializedObject, Dictionary<string, ReorderableList>>
        DefaultReorderableLists = new();
    public static int indentLevel { get; set; }
    public static Fix64 labelWidth { get; set; } = 150;
    public static Fix64 fieldWidth { get; set; } = 50;
    public static bool showMixedValue { get; set; }

    internal static object CaptureFeatureState() => new EditorGuiFeatureState(
        EnabledStack.Reverse().ToArray(),
        ChangedStack.Reverse().ToArray(),
        new Dictionary<int, int>(PendingPopupSelections),
        EditorObjectPicker.CaptureState(),
        DragAndDrop.CaptureState(),
        indentLevel,
        labelWidth,
        fieldWidth,
        showMixedValue);

    internal static void RestoreFeatureState(object state, bool restorePopupSelections)
    {
        if (state is not EditorGuiFeatureState snapshot) return;
        EnabledStack.Clear();
        foreach (var value in snapshot.EnabledValues) EnabledStack.Push(value);
        ChangedStack.Clear();
        foreach (var value in snapshot.ChangedValues) ChangedStack.Push(value);
        if (restorePopupSelections)
        {
            PendingPopupSelections.Clear();
            foreach (var pair in snapshot.PopupSelections)
                PendingPopupSelections[pair.Key] = pair.Value;
            EditorObjectPicker.RestoreState(snapshot.ObjectPickerState);
            DragAndDrop.RestoreState(snapshot.DragAndDropState);
        }
        indentLevel = snapshot.IndentLevel;
        labelWidth = snapshot.LabelWidth;
        fieldWidth = snapshot.FieldWidth;
        showMixedValue = snapshot.ShowMixedValue;
    }

    internal static void BeginEvent() => ObjectFieldOccurrences?.Clear();

    public static void BeginDisabledGroup(bool disabled)
    {
        EnabledStack.Push(GUI.enabled);
        GUI.enabled &= !disabled;
    }

    private readonly record struct EditorGuiFeatureState(
        bool[] EnabledValues,
        bool[] ChangedValues,
        IReadOnlyDictionary<int, int> PopupSelections,
        object ObjectPickerState,
        object DragAndDropState,
        int IndentLevel,
        Fix64 LabelWidth,
        Fix64 FieldWidth,
        bool ShowMixedValue);

    public static void EndDisabledGroup()
    {
        if (EnabledStack.Count > 0) GUI.enabled = EnabledStack.Pop();
    }

    public static void BeginChangeCheck()
    {
        ChangedStack.Push(GUI.changed);
        GUI.changed = false;
    }

    public static bool EndChangeCheck()
    {
        var changed = GUI.changed;
        var previous = ChangedStack.Count > 0 && ChangedStack.Pop();
        GUI.changed = changed || previous;
        return changed;
    }

    public static Rect PrefixLabel(Rect totalPosition, GUIContent label) =>
        PrefixLabel(totalPosition, label, null);

    public static Rect IndentedRect(Rect source)
    {
        var indentation = Fix64.Min(Fix64.Max(0, source.width),
            Fix64.Max(0, (Fix64)indentLevel * PropertyIndentWidth));
        return new Rect(source.x + indentation, source.y,
            Fix64.Max(0, source.width - indentation), source.height);
    }

    public static Rect PrefixLabel(Rect totalPosition, GUIContent label, GUIStyle? style)
    {
        var indented = IndentedRect(totalPosition);
        var prefixWidth = Fix64.Min(indented.width, labelWidth);
        var labelRect = new Rect(indented.x, indented.y, prefixWidth, indented.height);
        DrawHierarchyLabel(labelRect, label, style ?? EditorStyles.label);
        return new Rect(indented.x + prefixWidth, indented.y,
            Fix64.Max(0, indented.width - prefixWidth), indented.height);
    }

    public static void LabelField(Rect position, string label) => LabelField(position, label, null);
    public static void LabelField(Rect position, string label, GUIStyle? style) =>
        DrawHierarchyLabel(IndentedRect(position), new GUIContent(label), style ?? EditorStyles.label);
    public static void LabelField(Rect position, GUIContent label, GUIStyle? style = null) =>
        DrawHierarchyLabel(IndentedRect(position), label, style ?? EditorStyles.label);
    public static void SelectableLabel(Rect position, string text, GUIStyle? style = null) =>
        DrawHierarchyLabel(IndentedRect(position), new GUIContent(text), style ?? EditorStyles.label);
    public static bool Toggle(Rect position, bool value) => Toggle(position, value, (GUIStyle?)null);
    public static bool Toggle(Rect position, bool value, GUIStyle? style) =>
        GUI.Toggle(position, value, GUIContent.none, style ?? GUI.skin.toggle);
    public static bool Toggle(Rect position, string label, bool value) =>
        Toggle(position, label, value, null);
    public static bool Toggle(Rect position, string label, bool value, GUIStyle? style) =>
        GUI.Toggle(PrefixLabel(position, new GUIContent(label)), value, GUIContent.none,
            style ?? GUI.skin.toggle);
    public static string TextField(Rect position, string value) => TextField(position, value, (GUIStyle?)null);
    public static string TextField(Rect position, string value, GUIStyle? style) =>
        GUI.TextField(position, value, style: style ?? EditorStyles.textField);
    public static string TextField(Rect position, string label, string value) =>
        TextField(position, label, value, null);
    public static string TextField(Rect position, string label, string value, GUIStyle? style) =>
        GUI.TextField(PrefixLabel(position, new GUIContent(label)), value,
            style: style ?? EditorStyles.textField);
    public static string TextArea(Rect position, string value) => TextArea(position, value, null);
    public static string TextArea(Rect position, string value, GUIStyle? style) =>
        GUI.TextArea(position, value, style: style ?? EditorStyles.textArea);

    public static int IntField(Rect position, string label, int value) =>
        IntField(position, label, value, null);

    public static int IntField(Rect position, string label, int value, GUIStyle? style)
    {
        var text = GUI.TextField(PrefixLabel(position, new GUIContent(label)), FormatInteger(value),
            style: style ?? EditorStyles.numberField);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : value;
    }

    public static int IntField(Rect position, int value) => IntField(position, value, null);

    public static int IntField(Rect position, int value, GUIStyle? style)
    {
        var text = GUI.TextField(position, FormatInteger(value), style: style ?? EditorStyles.numberField);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : value;
    }

    public static float FloatField(Rect position, string label, float value) =>
        FloatField(position, label, value, null);

    public static float FloatField(Rect position, string label, float value, GUIStyle? style)
    {
        var text = GUI.TextField(PrefixLabel(position, new GUIContent(label)), FormatFloat(value),
            style: style ?? EditorStyles.numberField);
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : value;
    }

    public static float FloatField(Rect position, float value) => FloatField(position, value, null);

    public static float FloatField(Rect position, float value, GUIStyle? style)
    {
        var text = GUI.TextField(position, FormatFloat(value), style: style ?? EditorStyles.numberField);
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : value;
    }

    public static Color ColorField(Rect position, string label, Color value, bool showEyedropper = true,
        bool showAlpha = true, bool hdr = false) => ColorField(position, new GUIContent(label), value,
        showEyedropper, showAlpha, hdr, null);

    public static Color ColorField(Rect position, string label, Color value, GUIStyle? style) =>
        ColorField(position, new GUIContent(label), value, true, true, false, style);

    public static Color ColorField(Rect position, string label, Color value, bool showEyedropper,
        bool showAlpha, bool hdr, GUIStyle? style) => ColorField(position, new GUIContent(label), value,
        showEyedropper, showAlpha, hdr, style);

    public static Color ColorField(Rect position, GUIContent label, Color value, bool showEyedropper = true,
        bool showAlpha = true, bool hdr = false) => DoColorField(position, label, value, showEyedropper,
        showAlpha, hdr, usePrefix: true, style: null);

    public static Color ColorField(Rect position, GUIContent label, Color value, GUIStyle? style) =>
        DoColorField(position, label, value, true, true, false, usePrefix: true, style: style);

    public static Color ColorField(Rect position, GUIContent label, Color value, bool showEyedropper,
        bool showAlpha, bool hdr, GUIStyle? style) => DoColorField(position, label, value, showEyedropper,
        showAlpha, hdr, usePrefix: true, style: style);

    public static Color ColorField(Rect position, Color value, bool showEyedropper = true,
        bool showAlpha = true, bool hdr = false) => DoColorField(position, GUIContent.none, value,
        showEyedropper, showAlpha, hdr, usePrefix: false, style: null);

    public static Color ColorField(Rect position, Color value, GUIStyle? style) =>
        DoColorField(position, GUIContent.none, value, true, true, false, usePrefix: false, style: style);

    public static Color ColorField(Rect position, Color value, bool showEyedropper, bool showAlpha,
        bool hdr, GUIStyle? style) => DoColorField(position, GUIContent.none, value, showEyedropper,
        showAlpha, hdr, usePrefix: false, style: style);

    public static BObject? ObjectField(
        Rect position,
        BObject? value,
        Type objectType,
        bool allowSceneObjects) => ObjectField(position, value, objectType, allowSceneObjects, null);

    public static BObject? ObjectField(
        Rect position,
        BObject? value,
        Type objectType,
        bool allowSceneObjects,
        GUIStyle? style) =>
        DoObjectField(position, GUIContent.none, value, objectType, allowSceneObjects,
            usePrefix: false, showMixedValue, stableIdentity: 0, style, out _);

    public static BObject? ObjectField(
        Rect position,
        string label,
        BObject? value,
        Type objectType,
        bool allowSceneObjects) =>
        ObjectField(position, new GUIContent(label), value, objectType, allowSceneObjects, null);

    public static BObject? ObjectField(
        Rect position,
        string label,
        BObject? value,
        Type objectType,
        bool allowSceneObjects,
        GUIStyle? style) =>
        ObjectField(position, new GUIContent(label), value, objectType, allowSceneObjects, style);

    public static BObject? ObjectField(
        Rect position,
        GUIContent label,
        BObject? value,
        Type objectType,
        bool allowSceneObjects) => ObjectField(position, label, value, objectType, allowSceneObjects, null);

    public static BObject? ObjectField(
        Rect position,
        GUIContent label,
        BObject? value,
        Type objectType,
        bool allowSceneObjects,
        GUIStyle? style)
    {
        ArgumentNullException.ThrowIfNull(label);
        return DoObjectField(position, label, value, objectType, allowSceneObjects,
            usePrefix: true, showMixedValue, stableIdentity: 0, style, out _);
    }

    public static T? ObjectField<T>(Rect position, T? value, bool allowSceneObjects) where T : BObject =>
        (T?)ObjectField(position, value, typeof(T), allowSceneObjects);

    public static T? ObjectField<T>(Rect position, T? value, bool allowSceneObjects, GUIStyle? style)
        where T : BObject => (T?)ObjectField(position, value, typeof(T), allowSceneObjects, style);

    public static T? ObjectField<T>(Rect position, string label, T? value, bool allowSceneObjects)
        where T : BObject =>
        (T?)ObjectField(position, label, value, typeof(T), allowSceneObjects);

    public static T? ObjectField<T>(Rect position, string label, T? value, bool allowSceneObjects,
        GUIStyle? style) where T : BObject =>
        (T?)ObjectField(position, label, value, typeof(T), allowSceneObjects, style);

    public static T? ObjectField<T>(Rect position, GUIContent label, T? value, bool allowSceneObjects)
        where T : BObject =>
        (T?)ObjectField(position, label, value, typeof(T), allowSceneObjects);

    public static T? ObjectField<T>(Rect position, GUIContent label, T? value, bool allowSceneObjects,
        GUIStyle? style) where T : BObject =>
        (T?)ObjectField(position, label, value, typeof(T), allowSceneObjects, style);

    public static void ObjectField(
        Rect position,
        SerializedProperty property,
        Type objectType) => ObjectField(position, property, objectType, (GUIStyle?)null);

    public static void ObjectField(
        Rect position,
        SerializedProperty property,
        Type objectType,
        GUIStyle? style)
    {
        ArgumentNullException.ThrowIfNull(property);
        ObjectField(position, property, objectType,
            new GUIContent(property.displayName, tooltip: property.tooltip),
            AllowSceneObjects(property), style);
    }

    public static void ObjectField(
        Rect position,
        SerializedProperty property,
        Type objectType,
        bool allowSceneObjects) => ObjectField(position, property, objectType, allowSceneObjects, null);

    public static void ObjectField(
        Rect position,
        SerializedProperty property,
        Type objectType,
        bool allowSceneObjects,
        GUIStyle? style)
    {
        ArgumentNullException.ThrowIfNull(property);
        ObjectField(position, property, objectType,
            new GUIContent(property.displayName, tooltip: property.tooltip),
            allowSceneObjects, style);
    }

    public static void ObjectField(
        Rect position,
        SerializedProperty property,
        Type objectType,
        GUIContent label) =>
        ObjectField(position, property, objectType, label, AllowSceneObjects(property), null);

    public static void ObjectField(
        Rect position,
        SerializedProperty property,
        Type objectType,
        GUIContent label,
        GUIStyle? style) =>
        ObjectField(position, property, objectType, label, AllowSceneObjects(property), style);

    public static void ObjectField(
        Rect position,
        SerializedProperty property,
        Type objectType,
        string label) =>
        ObjectField(position, property, objectType, new GUIContent(label));

    public static void ObjectField(
        Rect position,
        SerializedProperty property,
        Type objectType,
        string label,
        GUIStyle? style) =>
        ObjectField(position, property, objectType, new GUIContent(label), style);

    public static void ObjectField(
        Rect position,
        SerializedProperty property,
        Type objectType,
        GUIContent label,
        bool allowSceneObjects) =>
        ObjectField(position, property, objectType, label, allowSceneObjects, null);

    public static void ObjectField(
        Rect position,
        SerializedProperty property,
        Type objectType,
        GUIContent label,
        bool allowSceneObjects,
        GUIStyle? style)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(label);
        if (property.propertyType != SerializedPropertyType.ObjectReference)
            throw new ArgumentException($"SerializedProperty '{property.propertyPath}' is not an object reference.",
                nameof(property));

        var effectiveType = EffectiveObjectType(property.valueType, objectType);
        BeginChangeCheck();
        var value = DoObjectField(position, label, property.objectReferenceValue, effectiveType,
            allowSceneObjects, usePrefix: true,
            mixed: property.hasMultipleDifferentValues || showMixedValue,
            stableIdentity: ObjectFieldIdentity(property), style, out var committed);
        _ = EndChangeCheck();
        if (committed) property.objectReferenceValue = value;
    }

    public static void ObjectField(
        Rect position,
        SerializedProperty property,
        Type objectType,
        string label,
        bool allowSceneObjects) =>
        ObjectField(position, property, objectType, new GUIContent(label), allowSceneObjects);

    public static void ObjectField(
        Rect position,
        SerializedProperty property,
        Type objectType,
        string label,
        bool allowSceneObjects,
        GUIStyle? style) =>
        ObjectField(position, property, objectType, new GUIContent(label), allowSceneObjects, style);

    private static BObject? DoObjectField(
        Rect position,
        GUIContent label,
        BObject? value,
        Type objectType,
        bool allowSceneObjects,
        bool usePrefix,
        bool mixed,
        int stableIdentity,
        GUIStyle? style,
        out bool committed)
    {
        style ??= EditorStyles.popup;
        EditorObjectPicker.ValidateValue(value, objectType);
        var field = usePrefix ? PrefixLabel(position, label) : position;
        // Consume a regular IMGUI id so ObjectField keeps its place in keyboard-control ordering, then use
        // a field-specific interaction id for state that must survive into the next event. A focused-window
        // identity is not sufficient: non-focused Inspector instances are drawn too, and would otherwise
        // share picker and drag state.
        _ = GUIUtility.GetControlID("ObjectField".GetHashCode(StringComparison.Ordinal),
            FocusType.Keyboard, field);
        var token = ObjectFieldInteractionId(field, label, objectType, stableIdentity);
        committed = false;

        if (EditorObjectPicker.TryConsume(token, objectType, allowSceneObjects, out var picked))
        {
            if (mixed || !EditorObjectPicker.SameObject(value, picked))
            {
                value = picked;
                committed = true;
                GUI.changed = true;
            }
        }
        if (EditorObjectPicker.TryHandleDrag(field, objectType, allowSceneObjects, out var dragged))
        {
            if (mixed || !EditorObjectPicker.SameObject(value, dragged))
            {
                value = dragged;
                committed = true;
                GUI.changed = true;
            }
        }

        var pickerWidth = Fix64.Min(18, field.width);
        var objectRect = new Rect(field.x, field.y, Fix64.Max(0, field.width - pickerWidth), field.height);
        var pickerRect = new Rect(objectRect.xMax, field.y, pickerWidth, field.height);
        var content = EditorObjectPicker.Content(value, objectType, mixed && !committed);
        var startedDrag = objectRect.width > 0 && EditorObjectPicker.TryStartDrag(objectRect, token, value);
        if (objectRect.width > 0 && GUI.Button(token, objectRect, content, style) && !startedDrag)
        {
            if (value is not null)
            {
                if (Event.current.clickCount >= 2) Selection.activeObject = value;
                else
                {
                    EditorGUIUtility.PingObject(value);
                }
            }
            else EditorObjectPicker.Open(token, field, value, objectType, allowSceneObjects);
        }
        if (pickerWidth > 0 && GUI.Button(pickerRect,
                new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.Browse, "Select object"), style))
            EditorObjectPicker.Open(token, field, value, objectType, allowSceneObjects);
        return value;
    }

    private static Color DoColorField(Rect position, GUIContent label, Color value, bool showEyedropper,
        bool showAlpha, bool hdr, bool usePrefix, GUIStyle? style)
    {
        style ??= EditorStyles.colorField;
        var field = usePrefix ? PrefixLabel(position, label) : position;
        var eyedropperWidth = showEyedropper ? Fix64.Min(20, field.width) : Fix64.Zero;
        var swatch = new Rect(field.x, field.y, Fix64.Max(0, field.width - eyedropperWidth), field.height);
        var eyedropper = new Rect(swatch.xMax, field.y, eyedropperWidth, field.height);
        var id = GUIUtility.GetControlID("ColorField".GetHashCode(StringComparison.Ordinal),
            FocusType.Keyboard, field);
        var token = ControlToken(id, label.text);
        if (EditorColorPicker.TryConsume(token, out var picked))
        {
            value = showAlpha ? picked : new Color(picked.r, picked.g, picked.b, value.a);
            GUI.changed = true;
        }
        if (GUI.Button(swatch, new GUIContent(string.Empty, tooltip: showAlpha
                ? "Open Color Picker (RGBA)" : "Open Color Picker (RGB)"), style))
            EditorColorPicker.Open(token, value, showAlpha, hdr);
        if (showEyedropper && GUI.Button(eyedropper,
                new GUIContent(string.Empty, tooltip: "Pick a color from the screen"), style))
            EditorColorPicker.Pick(token, value, showAlpha);
        DrawColorSwatch(swatch, value, showAlpha, hdr);
        if (showEyedropper) DrawEyedropperButton(eyedropper, style);
        return value;
    }

    public static int Popup(Rect position, string label, int selectedIndex, string[] displayedOptions)
        => Popup(position, label, selectedIndex, displayedOptions, null);

    public static int Popup(Rect position, string label, int selectedIndex, string[] displayedOptions,
        GUIStyle? style) => DrawPopup(position, label, selectedIndex, displayedOptions,
        forceAdvanced: false, style);

    public static int AdvancedPopup(Rect position, string label, int selectedIndex,
        string[] displayedOptions) => AdvancedPopup(position, label, selectedIndex, displayedOptions, null);

    public static int AdvancedPopup(Rect position, string label, int selectedIndex,
        string[] displayedOptions, GUIStyle? style) => DrawPopup(position, label, selectedIndex,
        displayedOptions, forceAdvanced: true, style);

    public static bool DropDownButton(Rect position, string text, FocusType focusType,
        GUIStyle? style = null) => DropDownButton(position, new GUIContent(text), focusType, style);

    public static bool DropDownButton(Rect position, string text, GUIStyle? style = null) =>
        DropDownButton(position, new GUIContent(text), FocusType.Keyboard, style);

    public static bool DropDownButton(Rect position, GUIContent content, GUIStyle? style = null) =>
        DropDownButton(position, content, FocusType.Keyboard, style);

    public static bool DropDownButton(Rect position, GUIContent content, FocusType focusType,
        GUIStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        style ??= EditorStyles.dropDownButton;
        var pressed = GUI.Button(position, content, style, focusType);
        var arrowWidth = Fix64.Min(20, Fix64.Max(0, position.width));
        var arrow = new Rect(position.xMax - arrowWidth, position.y, arrowWidth, position.height);
        if (Event.current.type == EventType.Repaint && arrowWidth > 0)
        {
            var separator = style.normal.borderColor.a > 0
                ? style.normal.borderColor
                : EditorStyles.separator.normal.backgroundColor;
            GUI.DrawRect(new Rect(arrow.x, arrow.y + 1, 1, Fix64.Max(0, arrow.height - 2)),
                separator);
        }
        GUI.Label(arrow, new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.FoldoutOpen, string.Empty),
            EditorStyles.label);
        return pressed;
    }

    public static bool DropdownButton(Rect position, string text, FocusType focusType,
        GUIStyle? style = null) => DropDownButton(position, text, focusType, style);

    public static bool DropdownButton(Rect position, GUIContent content, FocusType focusType,
        GUIStyle? style = null) => DropDownButton(position, content, focusType, style);

    public static bool DropdownButton(Rect position, string text, GUIStyle? style = null) =>
        DropDownButton(position, text, FocusType.Keyboard, style);

    public static bool DropdownButton(Rect position, GUIContent content, GUIStyle? style = null) =>
        DropDownButton(position, content, FocusType.Keyboard, style);

    private static int DrawPopup(Rect position, string label, int selectedIndex,
        string[] displayedOptions, bool forceAdvanced, GUIStyle? style)
    {
        style ??= EditorStyles.popup;
        var field = PrefixLabel(position, new GUIContent(label));
        selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, displayedOptions.Length - 1));
        var id = GUIUtility.GetControlID("Popup".GetHashCode(StringComparison.Ordinal), FocusType.Keyboard, field);
        var token = ControlToken(id, label);
        if (PendingPopupSelections.Remove(token, out var pending))
        {
            selectedIndex = Math.Clamp(pending, 0, Math.Max(0, displayedOptions.Length - 1));
            GUI.changed = true;
        }
        var caption = displayedOptions.ElementAtOrDefault(selectedIndex) ?? "None";
        if (GUI.Button(field, new GUIContent(caption), style) && displayedOptions.Length > 0)
        {
            var menu = new GenericMenu();
            for (var index = 0; index < displayedOptions.Length; index++)
            {
                var captured = index;
                menu.AddItem(new GUIContent(displayedOptions[index]), index == selectedIndex, () =>
                {
                    PendingPopupSelections[token] = captured;
                    EditorApplication.QueuePlayerLoopUpdate();
                });
            }
            if (forceAdvanced) menu.ShowAsAdvancedDropdown(field);
            else menu.DropDown(field);
        }
        GUI.Label(new Rect(field.xMax - 18, field.y, 16, field.height),
            new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.FoldoutOpen, "Open dropdown"));
        return selectedIndex;
    }

    public static Enum EnumPopup(Rect position, string label, Enum selected) =>
        EnumPopup(position, label, selected, null);

    public static Enum EnumPopup(Rect position, string label, Enum selected, GUIStyle? style)
    {
        var names = Enum.GetNames(selected.GetType());
        var index = Array.IndexOf(names, selected.ToString());
        return (Enum)Enum.Parse(selected.GetType(), names[
            Popup(position, label, Math.Max(0, index), names, style)]);
    }

    public static Vector2 Vector2Field(Rect position, string label, Vector2 value) =>
        Vector2Field(position, label, value, null);

    public static Vector2 Vector2Field(Rect position, string label, Vector2 value, GUIStyle? style)
    {
        style ??= EditorStyles.numberField;
        position = WithVisibleWidth(position);
        if (position.height >= LinesHeight(3))
        {
            var rows = PrepareVerticalVectorField(position, label);
            return new Vector2((Fix64)AxisFloatField(rows, "X", value.x, style),
                (Fix64)AxisFloatField(NextLine(rows), "Y", value.y, style));
        }
        var field = PrepareVectorField(position, label, GetVectorMinimumFieldWidth(2));
        return new Vector2(
            (Fix64)AxisFloatField(HorizontalAxisRect(field, 0, 2), "X", value.x, style),
            (Fix64)AxisFloatField(HorizontalAxisRect(field, 1, 2), "Y", value.y, style));
    }

    public static Vector4 Vector4Field(Rect position, string label, Vector4 value) =>
        Vector4Field(position, label, value, null);

    public static Vector4 Vector4Field(Rect position, string label, Vector4 value, GUIStyle? style)
    {
        style ??= EditorStyles.numberField;
        position = WithVisibleWidth(position);
        if (position.height >= LinesHeight(5))
        {
            var rows = PrepareVerticalVectorField(position, label);
            return new Vector4(
                (Fix64)AxisFloatField(rows, "X", value.x, style),
                (Fix64)AxisFloatField(NextLine(rows), "Y", value.y, style),
                (Fix64)AxisFloatField(NextLine(NextLine(rows)), "Z", value.z, style),
                (Fix64)AxisFloatField(NextLine(NextLine(NextLine(rows))), "W", value.w, style));
        }
        var field = PrepareVectorField(position, label, GetVectorMinimumFieldWidth(4));
        return new Vector4(
            (Fix64)AxisFloatField(HorizontalAxisRect(field, 0, 4), "X", value.x, style),
            (Fix64)AxisFloatField(HorizontalAxisRect(field, 1, 4), "Y", value.y, style),
            (Fix64)AxisFloatField(HorizontalAxisRect(field, 2, 4), "Z", value.z, style),
            (Fix64)AxisFloatField(HorizontalAxisRect(field, 3, 4), "W", value.w, style));
    }

    public static float Slider(Rect position, string label, float value, float leftValue, float rightValue) =>
        Slider(position, label, value, leftValue, rightValue, null);

    public static float Slider(Rect position, string label, float value, float leftValue, float rightValue,
        GUIStyle? style)
    {
        style ??= GUI.skin.horizontalSlider;
        var field = PrefixLabel(position, new GUIContent(label));
        var numericWidth = Fix64.Clamp(field.width * Fix64.FromDecimal(0.28m), 46, 68);
        var slider = new Rect(field.x, field.y + 4, Fix64.Max(0, field.width - numericWidth - 5),
            Fix64.Max(8, field.height - 8));
        var numeric = new Rect(slider.xMax + 5, field.y, numericWidth, field.height);
        value = (float)GUI.HorizontalSlider(slider, (Fix64)value, (Fix64)leftValue, (Fix64)rightValue,
            style, GUI.skin.horizontalSliderThumb);
        value = FloatField(numeric, value);
        return Math.Clamp(value, Math.Min(leftValue, rightValue), Math.Max(leftValue, rightValue));
    }

    public static int IntSlider(Rect position, string label, int value, int leftValue, int rightValue) =>
        IntSlider(position, label, value, leftValue, rightValue, null);

    public static int IntSlider(Rect position, string label, int value, int leftValue, int rightValue,
        GUIStyle? style)
    {
        style ??= GUI.skin.horizontalSlider;
        var field = PrefixLabel(position, new GUIContent(label));
        var numericWidth = Fix64.Clamp(field.width * Fix64.FromDecimal(0.28m), 42, 62);
        var slider = new Rect(field.x, field.y + 4, Fix64.Max(0, field.width - numericWidth - 5),
            Fix64.Max(8, field.height - 8));
        var numeric = new Rect(slider.xMax + 5, field.y, numericWidth, field.height);
        value = (int)Math.Round((double)GUI.HorizontalSlider(slider, value, leftValue, rightValue,
            style, GUI.skin.horizontalSliderThumb));
        value = IntField(numeric, value);
        return Math.Clamp(value, Math.Min(leftValue, rightValue), Math.Max(leftValue, rightValue));
    }

    public static bool Foldout(Rect position, bool foldout, string content, bool toggleOnLabelClick = false) =>
        Foldout(position, foldout, content, toggleOnLabelClick, null);

    public static bool Foldout(Rect position, bool foldout, string content, GUIStyle? style) =>
        Foldout(position, foldout, content, false, style);

    public static bool Foldout(Rect position, bool foldout, string content, bool toggleOnLabelClick,
        GUIStyle? style)
    {
        _ = toggleOnLabelClick;
        position = IndentedRect(position);
        var icon = foldout ? EditorBuiltinIcons.Toolbar.FoldoutOpen : EditorBuiltinIcons.Toolbar.FoldoutClosed;
        if (GUI.Button(position, new GUIContent(content, icon, foldout ? "Collapse" : "Expand"),
                style ?? EditorStyles.foldout)) foldout = !foldout;
        return foldout;
    }

    public static void HelpBox(Rect position, string message, MessageType type) =>
        HelpBox(position, message, type, null);

    public static void HelpBox(Rect position, string message, MessageType type, GUIStyle? style)
    {
        position = IndentedRect(position);
        var icon = type switch
        {
            MessageType.Warning => EditorBuiltinIcons.Toolbar.Warning,
            MessageType.Error => EditorBuiltinIcons.Toolbar.Error,
            MessageType.Info => EditorBuiltinIcons.Toolbar.Info,
            _ => string.Empty
        };
        GUI.Box(position, new GUIContent(message, icon, type.ToString()), style ?? EditorStyles.helpBox);
    }

    public static bool PropertyField(Rect position, SerializedProperty property, GUIContent? label = null,
        bool includeChildren = false) => PropertyField(position, property, label, includeChildren, null);

    public static bool PropertyField(Rect position, SerializedProperty property, GUIStyle? style) =>
        PropertyField(position, property, null, false, style);

    public static bool PropertyField(Rect position, SerializedProperty property, GUIContent? label,
        GUIStyle? style) => PropertyField(position, property, label, false, style);

    public static bool PropertyField(Rect position, SerializedProperty property, GUIContent? label,
        bool includeChildren, GUIStyle? style)
    {
        ArgumentNullException.ThrowIfNull(property);
        label ??= new GUIContent(property.displayName, tooltip: property.tooltip);
        return DrawPropertyField(position, property, label, includeChildren, style, 0,
            new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    public static Fix64 GetPropertyHeight(SerializedProperty property, GUIContent? label = null,
        bool includeChildren = false)
    {
        ArgumentNullException.ThrowIfNull(property);
        var content = label ?? new GUIContent(property.displayName, tooltip: property.tooltip);
        return GetPropertyHeight(property, content, includeChildren, 0,
            new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    public static bool DefaultPropertyField(Rect position, SerializedProperty property, GUIContent label,
        bool includeChildren) => DefaultPropertyField(position, property, label, includeChildren, null);

    public static bool DefaultPropertyField(Rect position, SerializedProperty property, GUIContent label,
        GUIStyle? style) => DefaultPropertyField(position, property, label, false, style);

    public static bool DefaultPropertyField(Rect position, SerializedProperty property, GUIContent label,
        bool includeChildren, GUIStyle? style)
        => DrawDefaultPropertyField(position, property, label, includeChildren, style, 0,
            new HashSet<object>(ReferenceEqualityComparer.Instance));

    private static bool DrawPropertyField(Rect position, SerializedProperty property, GUIContent label,
        bool includeChildren, GUIStyle? style, int nestingDepth, HashSet<object> ancestors)
    {
        if (PropertyDrawerRegistry.TryCreate(property, out var drawer) &&
            EditorFeatureGuard.Invoke(
                $"PropertyDrawer {drawer.GetType().FullName}.{nameof(PropertyDrawer.OnGUI)}",
                () => drawer.OnGUI(position, property, label)))
            return property.isExpanded;
        return DrawDefaultPropertyField(position, property, label, includeChildren, style,
            nestingDepth, ancestors);
    }

    private static bool DrawDefaultPropertyField(Rect position, SerializedProperty property, GUIContent label,
        bool includeChildren, GUIStyle? style, int nestingDepth, HashSet<object> ancestors)
    {
        var oldEnabled = GUI.enabled;
        GUI.enabled &= property.editable;
        try
        {
            var ownHeight = GetSinglePropertyHeight(property);
            var row = new Rect(position.x, position.y, position.width, ownHeight);
            if (property.isArray)
            {
                property.isExpanded = Foldout(row, property.isExpanded, label.text,
                    toggleOnLabelClick: true, style);
                if (!includeChildren || !property.isExpanded) return property.isExpanded;

                var arrayIndent = indentLevel;
                try
                {
                    indentLevel = arrayIndent + 1;
                    var list = GetDefaultReorderableList(property);
                    var listHeight = (Fix64)list.GetHeight();
                    list.DoList(new Rect(position.x,
                        row.yMax + EditorGUIUtility.standardVerticalSpacing, position.width, listHeight));
                }
                finally { indentLevel = arrayIndent; }
                return property.isExpanded;
            }
            var children = GetExpandableChildren(property, nestingDepth, ancestors);
            if (children.Count > 0)
            {
                property.isExpanded = Foldout(row, property.isExpanded, label.text,
                    toggleOnLabelClick: true, style);
            }
            else
            {
                DrawSinglePropertyField(row, property, label, style);
            }

            if (!includeChildren || !property.isExpanded || children.Count == 0) return property.isExpanded;
            if (!property.TryGetBoxedValue(out var container)) return property.isExpanded;
            var tracked = TryTrackContainer(container, ancestors);
            var y = row.yMax + EditorGUIUtility.standardVerticalSpacing;
            var previousIndent = indentLevel;
            try
            {
                indentLevel = previousIndent + 1;
                foreach (var child in children)
                {
                    var childLabel = new GUIContent(child.displayName, tooltip: child.tooltip);
                    var childHeight = GetPropertyHeight(child, childLabel, includeChildren: true,
                        nestingDepth + 1, ancestors);
                    DrawPropertyField(new Rect(position.x, y, position.width, childHeight), child, childLabel,
                        includeChildren: true, style, nestingDepth + 1, ancestors);
                    y += childHeight + EditorGUIUtility.standardVerticalSpacing;
                }
            }
            finally
            {
                indentLevel = previousIndent;
                if (tracked) ancestors.Remove(container!);
            }
            return property.isExpanded;
        }
        finally { GUI.enabled = oldEnabled; }
    }

    private static void DrawSinglePropertyField(Rect position, SerializedProperty property, GUIContent label,
        GUIStyle? style)
    {
        var feature = $"Inspector property {property.serializedObject.targetObject.GetType().FullName}." +
                      $"{property.propertyPath}";
        if (EditorFeatureGuard.Invoke(feature,
                () => DrawSinglePropertyFieldUnsafe(position, property, label, style))) return;

        GUI.Label(PrefixLabel(position, label), new GUIContent("Unavailable",
            "The property getter failed. See the Console for details."), style ?? EditorStyles.label);
    }

    private static void DrawSinglePropertyFieldUnsafe(Rect position, SerializedProperty property, GUIContent label,
        GUIStyle? style)
    {
        var range = property.GetAttribute<RangeAttribute>();
        var previousMixedValue = showMixedValue;
        showMixedValue |= property.hasMultipleDifferentValues;
        try
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    ApplyControlValue(
                        () => Toggle(position, label.text, property.boolValue, style),
                        value => property.boolValue = value);
                    break;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                    ApplyControlValue(
                        () => range is null
                            ? IntField(position, label.text, property.intValue, style)
                            : IntSlider(position, label.text, property.intValue,
                                (int)MathF.Ceiling(range.min), (int)MathF.Floor(range.max), style),
                        value => property.intValue = value);
                    break;
                case SerializedPropertyType.Float:
                    ApplyControlValue(
                        () => range is null
                            ? FloatField(position, label.text, property.floatValue, style)
                            : Slider(position, label.text, property.floatValue, range.min, range.max, style),
                        value => property.floatValue = value);
                    break;
                case SerializedPropertyType.String:
                    ApplyControlValue(
                        () => TextField(position, label.text, property.stringValue, style),
                        value => property.stringValue = value);
                    break;
                case SerializedPropertyType.Color:
                    var usage = property.GetAttribute<ColorUsageAttribute>();
                    ApplyControlValue(
                        () => ColorField(position, label.text, property.colorValue,
                            true, usage?.showAlpha ?? true, usage?.hdr ?? false, style),
                        value => property.colorValue = value);
                    break;
                case SerializedPropertyType.Enum:
                    ApplyControlValue(
                        () => Popup(position, label.text, property.enumValueIndex,
                            property.enumDisplayNames, style),
                        value => property.enumValueIndex = value);
                    break;
                case SerializedPropertyType.Vector2:
                    ApplyControlValue(
                        () => Vector2Field(position, label.text, property.vector2Value, style),
                        value => property.vector2Value = value);
                    break;
                case SerializedPropertyType.Vector4:
                    ApplyControlValue(
                        () => Vector4Field(position, label.text, property.vector4Value, style),
                        value => property.vector4Value = value);
                    break;
                case SerializedPropertyType.ObjectReference:
                    ObjectField(position, property, property.valueType, label, style);
                    break;
                default:
                    GUI.Label(PrefixLabel(position, label), property.boxedValue?.ToString() ?? "None",
                        style ?? EditorStyles.label);
                    break;
            }
        }
        finally { showMixedValue = previousMixedValue; }
    }

    private static void ApplyControlValue<T>(Func<T> drawControl, Action<T> applyValue)
    {
        BeginChangeCheck();
        T value;
        try { value = drawControl(); }
        catch
        {
            _ = EndChangeCheck();
            throw;
        }
        if (EndChangeCheck()) applyValue(value);
    }

    private static Fix64 GetPropertyHeight(SerializedProperty property, GUIContent label,
        bool includeChildren, int nestingDepth, HashSet<object> ancestors)
    {
        if (PropertyDrawerRegistry.TryCreate(property, out var drawer) &&
            EditorFeatureGuard.TryInvoke(
                $"PropertyDrawer {drawer.GetType().FullName}.{nameof(PropertyDrawer.GetPropertyHeight)}",
                () => drawer.GetPropertyHeight(property, label), EditorGUIUtility.singleLineHeight,
                out var drawerHeight))
            return drawerHeight;

        var height = GetSinglePropertyHeight(property);
        if (!includeChildren || !property.isExpanded) return height;
        if (property.isArray)
        {
            var previousIndent = indentLevel;
            try
            {
                indentLevel = previousIndent + 1;
                return height + EditorGUIUtility.standardVerticalSpacing +
                       (Fix64)GetDefaultReorderableList(property).GetHeight();
            }
            finally { indentLevel = previousIndent; }
        }
        var children = GetExpandableChildren(property, nestingDepth, ancestors);
        if (children.Count == 0) return height;

        if (!property.TryGetBoxedValue(out var container)) return height;
        var tracked = TryTrackContainer(container, ancestors);
        try
        {
            foreach (var child in children)
            {
                height += EditorGUIUtility.standardVerticalSpacing;
                height += GetPropertyHeight(child,
                    new GUIContent(child.displayName, tooltip: child.tooltip), includeChildren: true,
                    nestingDepth + 1, ancestors);
            }
            return height;
        }
        finally
        {
            if (tracked) ancestors.Remove(container!);
        }
    }

    private static Fix64 GetSinglePropertyHeight(SerializedProperty property) => property.propertyType switch
    {
        SerializedPropertyType.Vector2 => GetVectorFieldHeight(2),
        SerializedPropertyType.Vector4 => GetVectorFieldHeight(4),
        _ => EditorGUIUtility.singleLineHeight
    };

    private static IReadOnlyList<SerializedProperty> GetExpandableChildren(SerializedProperty property,
        int nestingDepth, HashSet<object> ancestors)
    {
        if (nestingDepth >= MaxNestedPropertyDepth ||
            !property.TryGetBoxedValue(out var candidate) || candidate is not { } value) return [];
        if (!value.GetType().IsValueType && ancestors.Contains(value)) return [];
        return property.GetVisibleChildren();
    }

    private static bool TryTrackContainer(object? value, HashSet<object> ancestors) =>
        value is not null && !value.GetType().IsValueType && ancestors.Add(value);

    private static ReorderableList GetDefaultReorderableList(SerializedProperty property)
    {
        var lists = DefaultReorderableLists.GetOrCreateValue(property.serializedObject);
        if (lists.TryGetValue(property.propertyPath, out var existing))
        {
            existing.serializedProperty = property;
            return existing;
        }
        var created = new ReorderableList(property.serializedObject, property,
            draggable: true, displayHeader: false, displayAddButton: true, displayRemoveButton: true)
        {
            elementHeight = (float)EditorGUIUtility.singleLineHeight,
            footerHeight = (float)EditorGUIUtility.singleLineHeight,
            showDefaultBackground = true
        };
        lists[property.propertyPath] = created;
        return created;
    }

    internal static string FormatFloat(float value) => Math.Abs(value) < 0.00005f
        ? "0"
        : value.ToString($"0.{new string('#', NumericDecimals)}", CultureInfo.InvariantCulture);

    internal static Fix64 GetVectorFieldHeight(int dimensions)
    {
        var indentation = Fix64.Max(0, (Fix64)indentLevel * PropertyIndentWidth);
        var availableWidth = Fix64.Max(0, GUI.visibleViewWidth - 8 - indentation);
        if (dimensions is < 2 or > 4) throw new ArgumentOutOfRangeException(nameof(dimensions));
        var minimumFieldWidth = GetVectorMinimumFieldWidth(dimensions);
        var result = availableWidth < minimumFieldWidth
            ? LinesHeight(dimensions + 1)
            : availableWidth < minimumFieldWidth + VectorLabelMinimumWidth
            ? LinesHeight(2)
            : EditorGUIUtility.singleLineHeight;
        return result;
    }

    private static string FormatInteger(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static Rect PrepareVectorField(Rect position, string label, Fix64 minimumFieldWidth)
    {
        var indented = IndentedRect(position);
        if (position.height >= EditorGUIUtility.singleLineHeight * 2)
        {
            var labelRect = new Rect(indented.x, indented.y, indented.width,
                EditorGUIUtility.singleLineHeight);
            DrawHierarchyLabel(labelRect, new GUIContent(label), EditorStyles.label);
            return new Rect(indented.x,
                position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing,
                indented.width, EditorGUIUtility.singleLineHeight);
        }
        var measuredLabelWidth = GUITextMetrics.MeasureWidth(label, EditorStyles.label.fontSize,
            GUIUtility.fontFamily) + 8;
        var desired = Fix64.Max(labelWidth, measuredLabelWidth);
        var compactMinimum = Fix64.Min(VectorCompactLabelWidth, indented.width);
        var availableLabel = Fix64.Clamp(indented.width - minimumFieldWidth,
            compactMinimum, indented.width);
        var width = Fix64.Min(desired, availableLabel);
        var compactLabelRect = new Rect(indented.x, indented.y, width, indented.height);
        DrawHierarchyLabel(compactLabelRect, new GUIContent(label), EditorStyles.label);
        return new Rect(indented.x + width, indented.y,
            Fix64.Max(0, indented.width - width), indented.height);
    }

    private static Rect PrepareVerticalVectorField(Rect position, string label)
    {
        var indented = IndentedRect(position);
        var labelRect = new Rect(indented.x, indented.y, indented.width,
            EditorGUIUtility.singleLineHeight);
        DrawHierarchyLabel(labelRect, new GUIContent(label), EditorStyles.label);
        return new Rect(indented.x,
            position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing,
            indented.width, EditorGUIUtility.singleLineHeight);
    }

    private static void DrawHierarchyLabel(Rect position, GUIContent content, GUIStyle style)
    {
        var reserveFoldoutSpace = indentLevel > 0 && string.IsNullOrWhiteSpace(content.image) &&
                                  style.alignment is TextAnchor.UpperLeft or TextAnchor.MiddleLeft or
                                      TextAnchor.LowerLeft;
        GUI.Label(position, content, style,
            reserveFoldoutSpace ? GUI.ContentImageTextOffset : Fix64.Zero);
    }

    private static Rect WithVisibleWidth(Rect position) => new(position.x, position.y,
        Fix64.Min(position.width, GUI.GetVisibleWidth(position)), position.height);

    private static Rect NextLine(Rect position) => new(position.x,
        position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing,
        position.width, EditorGUIUtility.singleLineHeight);

    private static Fix64 LinesHeight(int lineCount) => EditorGUIUtility.singleLineHeight * lineCount +
        EditorGUIUtility.standardVerticalSpacing * (lineCount - 1);

    private static Rect HorizontalAxisRect(Rect field, int index, int dimensions)
    {
        var spacingCount = Math.Max(0, dimensions - 1);
        var totalSpacing = Fix64.Min(field.width, VectorAxisSpacing * spacingCount);
        var spacing = spacingCount > 0 ? totalSpacing / spacingCount : Fix64.Zero;
        var width = Fix64.Max(0, (field.width - totalSpacing) / dimensions);
        return new Rect(field.x + (width + spacing) * index, field.y, width, field.height);
    }

    private static float AxisFloatField(Rect position, string axis, Fix64 value, GUIStyle? style)
    {
        var desiredWidth = GetVectorAxisLabelWidth(axis);
        var axisWidth = Fix64.Min(position.width, desiredWidth);
        GUI.Label(new Rect(position.x, position.y, axisWidth, position.height), axis,
            EditorStyles.vectorAxisLabel);
        var gap = position.width > axisWidth ? Fix64.One : Fix64.Zero;
        return FloatField(new Rect(position.x + axisWidth + gap, position.y,
            Fix64.Max(0, position.width - axisWidth - gap), position.height), (float)value, style);
    }

    private static Fix64 GetVectorAxisLabelWidth(string axis) => Fix64.Max(VectorAxisLabelMinimumWidth,
        GUITextMetrics.MeasureWidth(axis, EditorStyles.vectorAxisLabel.fontSize, GUIUtility.fontFamily));

    private static Fix64 GetVectorMinimumFieldWidth(int dimensions)
    {
        var width = VectorAxisSpacing * (dimensions - 1);
        for (var index = 0; index < dimensions; index++)
            width += GetVectorAxisLabelWidth(VectorAxisNames[index]) + 1 + VectorNumericMinimumWidth;
        return width;
    }

    private static void DrawColorSwatch(Rect field, Color value, bool showAlpha, bool hdr)
    {
        if (Event.current.type != EventType.Repaint) return;
        var inner = new Rect(field.x + 1, field.y + 1, Fix64.Max(0, field.width - 2),
            Fix64.Max(0, field.height - 2));
        var preview = EditorColorMath.OpaquePreview(value);
        if (!GUI.enabled) preview *= new Color(Fix64.FromDecimal(.55m), Fix64.FromDecimal(.55m),
            Fix64.FromDecimal(.55m), 1);
        GUI.DrawRect(inner, preview);
        if (showAlpha)
        {
            var alphaHeight = Fix64.Clamp(inner.height * Fix64.FromDecimal(.2m), 2, 20);
            var alphaRect = new Rect(inner.x, inner.yMax - alphaHeight, inner.width, alphaHeight);
            GUI.DrawRect(alphaRect, Color.black);
            GUI.DrawRect(new Rect(alphaRect.x, alphaRect.y,
                alphaRect.width * Fix64.Clamp(value.a, 0, 1), alphaRect.height), Color.white);
        }
        if (hdr && (value.r > 1 || value.g > 1 || value.b > 1))
            GUI.Label(new Rect(inner.x + 2, inner.y, Fix64.Min(28, inner.width), inner.height),
                new GUIContent("HDR"), EditorStyles.miniLabel);
        if (showMixedValue)
            GUI.Label(inner, new GUIContent("-"), EditorStyles.boldLabel);
    }

    private static void DrawEyedropperButton(Rect rect, GUIStyle style)
    {
        if (Event.current.type != EventType.Repaint || rect.width <= 0) return;
        var tint = GetStyleState(style, rect).textColor;
        var startX = rect.x + (rect.width - 12) / 2;
        var startY = rect.y + (rect.height - 12) / 2;
        for (var index = 0; index < 6; index++)
            GUI.DrawRect(new Rect(startX + index, startY + 8 - index, 2, 2), tint);
        GUI.DrawRect(new Rect(startX + 7, startY + 1, 4, 4), tint);
        GUI.DrawRect(new Rect(startX + 1, startY + 9, 3, 2), tint);
    }

    private static GUIStyleState GetStyleState(GUIStyle style, Rect position)
    {
        if (!GUI.enabled) return style.disabled;
        var hovered = position.Contains(Event.current.mousePosition);
        if (hovered && GUIUtility.hotControl != 0) return style.active;
        if (GUIUtility.keyboardControl != 0) return style.focused;
        return hovered ? style.hover : style.normal;
    }

    private static int ControlToken(int id, string label) => HashCode.Combine(id, label);

    private static int ObjectFieldInteractionId(
        Rect field,
        GUIContent label,
        Type objectType,
        int stableIdentity)
    {
        var owner = EditorWindow.currentDrawingWindow;
        var ownerIdentity = owner is null ? 0 : RuntimeHelpers.GetHashCode(owner);
        var root = GUI.GUIToRootPoint(new Vector2(field.x, field.y));
        var structuralIdentity = stableIdentity != 0
            ? new ObjectFieldStructuralIdentity(ownerIdentity, stableIdentity, default, default,
                default, default, string.Empty, objectType)
            : new ObjectFieldStructuralIdentity(ownerIdentity, 0, root.x, root.y,
                field.width, field.height, label.text ?? string.Empty, objectType);
        var occurrences = ObjectFieldOccurrences ??= [];
        var occurrence = occurrences.GetValueOrDefault(structuralIdentity);
        occurrences[structuralIdentity] = occurrence + 1;

        var token = HashCode.Combine(0x4F424A46, structuralIdentity, occurrence);
        return token == 0 ? int.MinValue : token;
    }

    private readonly record struct ObjectFieldStructuralIdentity(
        int OwnerIdentity,
        int StableIdentity,
        Fix64 X,
        Fix64 Y,
        Fix64 Width,
        Fix64 Height,
        string Label,
        Type ObjectType);

    private static bool AllowSceneObjects(SerializedProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return property.serializedObject.targetObjects.All(target => !EditorUtility.IsPersistent(target));
    }

    private static int ObjectFieldIdentity(SerializedProperty property)
    {
        var hash = new HashCode();
        hash.Add(property.propertyPath, StringComparer.Ordinal);
        foreach (var target in property.serializedObject.targetObjects) hash.Add(target.Id);
        return hash.ToHashCode();
    }

    private static Type EffectiveObjectType(Type propertyType, Type requestedType)
    {
        ArgumentNullException.ThrowIfNull(propertyType);
        ArgumentNullException.ThrowIfNull(requestedType);
        if (!typeof(BObject).IsAssignableFrom(propertyType) ||
            !typeof(BObject).IsAssignableFrom(requestedType))
            throw new ArgumentException("ObjectField types must derive from BObject.", nameof(requestedType));
        if (propertyType.IsAssignableFrom(requestedType)) return requestedType;
        if (requestedType.IsAssignableFrom(propertyType)) return propertyType;
        throw new ArgumentException(
            $"Requested type {requestedType.FullName} is incompatible with property type {propertyType.FullName}.",
            nameof(requestedType));
    }

    public readonly struct DisabledScope : IDisposable
    {
        public DisabledScope(bool disabled) => BeginDisabledGroup(disabled);
        public void Dispose() => EndDisabledGroup();
    }

    public readonly struct ChangeCheckScope : IDisposable
    {
        private readonly bool _changedBefore;
        public ChangeCheckScope() { _changedBefore = GUI.changed; GUI.changed = false; }
        public bool changed => GUI.changed;
        public void Dispose() => GUI.changed |= _changedBefore;
    }
}
