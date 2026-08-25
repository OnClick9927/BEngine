using System.Globalization;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

public static class EditorGUI
{
    private const int NumericDecimals = 4;
    private static readonly Fix64 VectorAxisLabelMinimumWidth = 20;
    private static readonly Fix64 VectorNumericMinimumWidth = 66;
    private static readonly Fix64 VectorAxisSpacing = 1;
    private static readonly Fix64 VectorLabelMinimumWidth = 72;
    private static readonly Fix64 VectorCompactLabelWidth = 8;
    private static readonly string[] VectorAxisNames = ["X", "Y", "Z", "W"];
    private static readonly Stack<bool> EnabledStack = new();
    private static readonly Stack<bool> ChangedStack = new();
    private static readonly Dictionary<int, int> PendingPopupSelections = [];
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

    public static Rect PrefixLabel(Rect totalPosition, GUIContent label)
    {
        var left = Fix64.Min(totalPosition.width, labelWidth + indentLevel * 15);
        GUI.Label(new Rect(totalPosition.x + indentLevel * 15, totalPosition.y,
            Fix64.Max(0, left - indentLevel * 15), totalPosition.height), label);
        return new Rect(totalPosition.x + left, totalPosition.y,
            Fix64.Max(0, totalPosition.width - left), totalPosition.height);
    }

    public static void LabelField(Rect position, string label) => GUI.Label(position, label);
    public static void LabelField(Rect position, GUIContent label, GUIStyle? style = null) =>
        GUI.Label(position, label, style ?? EditorStyles.label);
    public static void SelectableLabel(Rect position, string text, GUIStyle? style = null) =>
        GUI.Label(position, text);
    public static bool Toggle(Rect position, bool value) => GUI.Toggle(position, value, GUIContent.none);
    public static bool Toggle(Rect position, string label, bool value) =>
        GUI.Toggle(PrefixLabel(position, new GUIContent(label)), value, GUIContent.none);
    public static string TextField(Rect position, string value) =>
        GUI.TextField(position, value, style: EditorStyles.textField);
    public static string TextField(Rect position, string label, string value) =>
        GUI.TextField(PrefixLabel(position, new GUIContent(label)), value, style: EditorStyles.textField);
    public static string TextArea(Rect position, string value) => GUI.TextArea(position, value);

    public static int IntField(Rect position, string label, int value)
    {
        var text = GUI.TextField(PrefixLabel(position, new GUIContent(label)), FormatInteger(value),
            style: EditorStyles.numberField);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : value;
    }

    public static int IntField(Rect position, int value)
    {
        var text = GUI.TextField(position, FormatInteger(value), style: EditorStyles.numberField);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : value;
    }

    public static float FloatField(Rect position, string label, float value)
    {
        var text = GUI.TextField(PrefixLabel(position, new GUIContent(label)), FormatFloat(value),
            style: EditorStyles.numberField);
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : value;
    }

    public static float FloatField(Rect position, float value)
    {
        var text = GUI.TextField(position, FormatFloat(value), style: EditorStyles.numberField);
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : value;
    }

    public static Color ColorField(Rect position, string label, Color value, bool showEyedropper = true,
        bool showAlpha = true, bool hdr = false) => ColorField(position, new GUIContent(label), value,
        showEyedropper, showAlpha, hdr);

    public static Color ColorField(Rect position, GUIContent label, Color value, bool showEyedropper = true,
        bool showAlpha = true, bool hdr = false) => DoColorField(position, label, value, showEyedropper,
        showAlpha, hdr, usePrefix: true);

    public static Color ColorField(Rect position, Color value, bool showEyedropper = true,
        bool showAlpha = true, bool hdr = false) => DoColorField(position, GUIContent.none, value,
        showEyedropper, showAlpha, hdr, usePrefix: false);

    public static BObject? ObjectField(
        Rect position,
        BObject? value,
        Type objectType,
        bool allowSceneObjects) =>
        DoObjectField(position, GUIContent.none, value, objectType, allowSceneObjects,
            usePrefix: false, showMixedValue, stableIdentity: 0, out _);

    public static BObject? ObjectField(
        Rect position,
        string label,
        BObject? value,
        Type objectType,
        bool allowSceneObjects) =>
        ObjectField(position, new GUIContent(label), value, objectType, allowSceneObjects);

    public static BObject? ObjectField(
        Rect position,
        GUIContent label,
        BObject? value,
        Type objectType,
        bool allowSceneObjects)
    {
        ArgumentNullException.ThrowIfNull(label);
        return DoObjectField(position, label, value, objectType, allowSceneObjects,
            usePrefix: true, showMixedValue, stableIdentity: 0, out _);
    }

    public static T? ObjectField<T>(Rect position, T? value, bool allowSceneObjects) where T : BObject =>
        (T?)ObjectField(position, value, typeof(T), allowSceneObjects);

    public static T? ObjectField<T>(Rect position, string label, T? value, bool allowSceneObjects)
        where T : BObject =>
        (T?)ObjectField(position, label, value, typeof(T), allowSceneObjects);

    public static T? ObjectField<T>(Rect position, GUIContent label, T? value, bool allowSceneObjects)
        where T : BObject =>
        (T?)ObjectField(position, label, value, typeof(T), allowSceneObjects);

    public static void ObjectField(
        Rect position,
        SerializedProperty property,
        Type objectType)
    {
        ArgumentNullException.ThrowIfNull(property);
        ObjectField(position, property, objectType,
            new GUIContent(property.displayName, tooltip: property.tooltip),
            AllowSceneObjects(property));
    }

    public static void ObjectField(
        Rect position,
        SerializedProperty property,
        Type objectType,
        bool allowSceneObjects)
    {
        ArgumentNullException.ThrowIfNull(property);
        ObjectField(position, property, objectType,
            new GUIContent(property.displayName, tooltip: property.tooltip),
            allowSceneObjects);
    }

    public static void ObjectField(
        Rect position,
        SerializedProperty property,
        Type objectType,
        GUIContent label) =>
        ObjectField(position, property, objectType, label, AllowSceneObjects(property));

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
        GUIContent label,
        bool allowSceneObjects)
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
            stableIdentity: ObjectFieldIdentity(property), out var committed);
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

    private static BObject? DoObjectField(
        Rect position,
        GUIContent label,
        BObject? value,
        Type objectType,
        bool allowSceneObjects,
        bool usePrefix,
        bool mixed,
        int stableIdentity,
        out bool committed)
    {
        EditorObjectPicker.ValidateValue(value, objectType);
        var field = usePrefix ? PrefixLabel(position, label) : position;
        var id = GUIUtility.GetControlID("ObjectField".GetHashCode(StringComparison.Ordinal),
            FocusType.Keyboard, field);
        var owner = EditorWindow.focusedWindow;
        var ownerIdentity = owner is null ? 0 : RuntimeHelpers.GetHashCode(owner);
        var token = HashCode.Combine(ControlToken(id, label.text), objectType, ownerIdentity, stableIdentity);
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

        var content = EditorObjectPicker.Content(value, objectType, mixed && !committed);
        if (GUI.Button(field, content, EditorStyles.popup))
            EditorObjectPicker.Open(token, field, value, objectType, allowSceneObjects);
        var pickerWidth = Fix64.Min(18, field.width);
        if (pickerWidth > 0)
            GUI.Label(new Rect(field.xMax - pickerWidth, field.y, pickerWidth, field.height),
                new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.Browse, "Select object"));
        return value;
    }

    private static Color DoColorField(Rect position, GUIContent label, Color value, bool showEyedropper,
        bool showAlpha, bool hdr, bool usePrefix)
    {
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
                ? "Open Color Picker (RGBA)" : "Open Color Picker (RGB)"), EditorStyles.colorField))
            EditorColorPicker.Open(token, value, showAlpha, hdr);
        if (showEyedropper && GUI.Button(eyedropper,
                new GUIContent(string.Empty, tooltip: "Pick a color from the screen"), EditorStyles.colorField))
            EditorColorPicker.Pick(token, value, showAlpha);
        DrawColorSwatch(swatch, value, showAlpha, hdr);
        if (showEyedropper) DrawEyedropperButton(eyedropper);
        return value;
    }

    public static int Popup(Rect position, string label, int selectedIndex, string[] displayedOptions)
        => DrawPopup(position, label, selectedIndex, displayedOptions, forceAdvanced: false);

    public static int AdvancedPopup(Rect position, string label, int selectedIndex,
        string[] displayedOptions)
        => DrawPopup(position, label, selectedIndex, displayedOptions, forceAdvanced: true);

    private static int DrawPopup(Rect position, string label, int selectedIndex,
        string[] displayedOptions, bool forceAdvanced)
    {
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
        if (GUI.Button(field, new GUIContent(caption), EditorStyles.popup) && displayedOptions.Length > 0)
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

    public static Enum EnumPopup(Rect position, string label, Enum selected)
    {
        var names = Enum.GetNames(selected.GetType());
        var index = Array.IndexOf(names, selected.ToString());
        return (Enum)Enum.Parse(selected.GetType(), names[Popup(position, label, Math.Max(0, index), names)]);
    }

    public static Vector2 Vector2Field(Rect position, string label, Vector2 value)
    {
        position = WithVisibleWidth(position);
        if (position.height >= LinesHeight(3))
        {
            var rows = PrepareVerticalVectorField(position, label);
            return new Vector2((Fix64)AxisFloatField(rows, "X", value.x),
                (Fix64)AxisFloatField(NextLine(rows), "Y", value.y));
        }
        var field = PrepareVectorField(position, label, GetVectorMinimumFieldWidth(2));
        return new Vector2(
            (Fix64)AxisFloatField(HorizontalAxisRect(field, 0, 2), "X", value.x),
            (Fix64)AxisFloatField(HorizontalAxisRect(field, 1, 2), "Y", value.y));
    }

    public static Vector4 Vector4Field(Rect position, string label, Vector4 value)
    {
        position = WithVisibleWidth(position);
        if (position.height >= LinesHeight(5))
        {
            var rows = PrepareVerticalVectorField(position, label);
            return new Vector4(
                (Fix64)AxisFloatField(rows, "X", value.x),
                (Fix64)AxisFloatField(NextLine(rows), "Y", value.y),
                (Fix64)AxisFloatField(NextLine(NextLine(rows)), "Z", value.z),
                (Fix64)AxisFloatField(NextLine(NextLine(NextLine(rows))), "W", value.w));
        }
        var field = PrepareVectorField(position, label, GetVectorMinimumFieldWidth(4));
        return new Vector4(
            (Fix64)AxisFloatField(HorizontalAxisRect(field, 0, 4), "X", value.x),
            (Fix64)AxisFloatField(HorizontalAxisRect(field, 1, 4), "Y", value.y),
            (Fix64)AxisFloatField(HorizontalAxisRect(field, 2, 4), "Z", value.z),
            (Fix64)AxisFloatField(HorizontalAxisRect(field, 3, 4), "W", value.w));
    }

    public static float Slider(Rect position, string label, float value, float leftValue, float rightValue)
    {
        var field = PrefixLabel(position, new GUIContent(label));
        var numericWidth = Fix64.Clamp(field.width * Fix64.FromDecimal(0.28m), 46, 68);
        var slider = new Rect(field.x, field.y + 4, Fix64.Max(0, field.width - numericWidth - 5),
            Fix64.Max(8, field.height - 8));
        var numeric = new Rect(slider.xMax + 5, field.y, numericWidth, field.height);
        value = (float)GUI.HorizontalSlider(slider, (Fix64)value, (Fix64)leftValue, (Fix64)rightValue);
        value = FloatField(numeric, value);
        return Math.Clamp(value, Math.Min(leftValue, rightValue), Math.Max(leftValue, rightValue));
    }

    public static int IntSlider(Rect position, string label, int value, int leftValue, int rightValue)
    {
        var field = PrefixLabel(position, new GUIContent(label));
        var numericWidth = Fix64.Clamp(field.width * Fix64.FromDecimal(0.28m), 42, 62);
        var slider = new Rect(field.x, field.y + 4, Fix64.Max(0, field.width - numericWidth - 5),
            Fix64.Max(8, field.height - 8));
        var numeric = new Rect(slider.xMax + 5, field.y, numericWidth, field.height);
        value = (int)Math.Round((double)GUI.HorizontalSlider(slider, value, leftValue, rightValue));
        value = IntField(numeric, value);
        return Math.Clamp(value, Math.Min(leftValue, rightValue), Math.Max(leftValue, rightValue));
    }

    public static bool Foldout(Rect position, bool foldout, string content, bool toggleOnLabelClick = false)
    {
        var icon = foldout ? EditorBuiltinIcons.Toolbar.FoldoutOpen : EditorBuiltinIcons.Toolbar.FoldoutClosed;
        if (GUI.Button(position, new GUIContent(content, icon, foldout ? "Collapse" : "Expand"),
                EditorStyles.foldout)) foldout = !foldout;
        return foldout;
    }

    public static void HelpBox(Rect position, string message, MessageType type)
    {
        var icon = type switch
        {
            MessageType.Warning => EditorBuiltinIcons.Toolbar.Warning,
            MessageType.Error => EditorBuiltinIcons.Toolbar.Error,
            MessageType.Info => EditorBuiltinIcons.Toolbar.Info,
            _ => string.Empty
        };
        GUI.Box(position, new GUIContent(message, icon, type.ToString()), EditorStyles.helpBox);
    }

    public static bool PropertyField(Rect position, SerializedProperty property, GUIContent? label = null,
        bool includeChildren = false)
    {
        ArgumentNullException.ThrowIfNull(property);
        label ??= new GUIContent(property.displayName, tooltip: property.tooltip);
        if (PropertyDrawerRegistry.TryCreate(property, out var drawer))
        {
            if (EditorFeatureGuard.Invoke(
                    $"PropertyDrawer {drawer.GetType().FullName}.{nameof(PropertyDrawer.OnGUI)}",
                    () => drawer.OnGUI(position, property, label)))
                return property.isExpanded;
        }
        return DefaultPropertyField(position, property, label, includeChildren);
    }

    public static Fix64 GetPropertyHeight(SerializedProperty property, GUIContent? label = null,
        bool includeChildren = false)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (PropertyDrawerRegistry.TryCreate(property, out var drawer))
        {
            var content = label ?? new GUIContent(property.displayName);
            if (EditorFeatureGuard.TryInvoke(
                    $"PropertyDrawer {drawer.GetType().FullName}.{nameof(PropertyDrawer.GetPropertyHeight)}",
                    () => drawer.GetPropertyHeight(property, content), EditorGUIUtility.singleLineHeight,
                    out var height))
                return height;
        }
        return property.propertyType switch
        {
            SerializedPropertyType.Vector2 => GetVectorFieldHeight(2),
            SerializedPropertyType.Vector4 => GetVectorFieldHeight(4),
            _ => EditorGUIUtility.singleLineHeight
        };
    }

    public static bool DefaultPropertyField(Rect position, SerializedProperty property, GUIContent label,
        bool includeChildren)
    {
        var oldEnabled = GUI.enabled;
        GUI.enabled &= property.editable;
        try
        {
            var range = property.GetAttribute<RangeAttribute>();
            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    property.boolValue = Toggle(position, label.text, property.boolValue); break;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                    property.intValue = range is null
                        ? IntField(position, label.text, property.intValue)
                        : IntSlider(position, label.text, property.intValue,
                            (int)MathF.Ceiling(range.min), (int)MathF.Floor(range.max));
                    break;
                case SerializedPropertyType.Float:
                    property.floatValue = range is null
                        ? FloatField(position, label.text, property.floatValue)
                        : Slider(position, label.text, property.floatValue, range.min, range.max);
                    break;
                case SerializedPropertyType.String:
                    property.stringValue = TextField(position, label.text, property.stringValue); break;
                case SerializedPropertyType.Color:
                    var usage = property.GetAttribute<ColorUsageAttribute>();
                    property.colorValue = ColorField(position, label.text, property.colorValue,
                        showAlpha: usage?.showAlpha ?? true, hdr: usage?.hdr ?? false);
                    break;
                case SerializedPropertyType.Enum:
                    property.enumValueIndex = Popup(position, label.text, property.enumValueIndex,
                        property.enumDisplayNames); break;
                case SerializedPropertyType.Vector2:
                    property.vector2Value = Vector2Field(position, label.text, property.vector2Value); break;
                case SerializedPropertyType.Vector4:
                    property.vector4Value = Vector4Field(position, label.text, property.vector4Value); break;
                case SerializedPropertyType.ObjectReference:
                    ObjectField(position, property, property.valueType, label); break;
                default:
                    GUI.Label(PrefixLabel(position, label), property.boxedValue?.ToString() ?? "None"); break;
            }
            return property.isExpanded;
        }
        finally { GUI.enabled = oldEnabled; }
    }

    internal static string FormatFloat(float value) => Math.Abs(value) < 0.00005f
        ? "0"
        : value.ToString($"0.{new string('#', NumericDecimals)}", CultureInfo.InvariantCulture);

    internal static Fix64 GetVectorFieldHeight(int dimensions)
    {
        var availableWidth = Fix64.Max(0, GUI.visibleViewWidth - 8);
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
        var indent = indentLevel * 15;
        if (position.height >= EditorGUIUtility.singleLineHeight * 2)
        {
            GUI.Label(new Rect(position.x + indent, position.y, Fix64.Max(0, position.width - indent),
                EditorGUIUtility.singleLineHeight), label);
            return new Rect(position.x + indent,
                position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing,
                Fix64.Max(0, position.width - indent), EditorGUIUtility.singleLineHeight);
        }
        var measuredLabelWidth = GUITextMetrics.MeasureWidth(label, EditorStyles.label.fontSize,
            GUIUtility.fontFamily) + 8;
        var desired = Fix64.Max(labelWidth, measuredLabelWidth) + indent;
        var availableLabel = Fix64.Max(VectorCompactLabelWidth + indent, position.width - minimumFieldWidth);
        var width = Fix64.Min(desired, availableLabel);
        GUI.Label(new Rect(position.x + indent, position.y, Fix64.Max(0, width - indent), position.height), label);
        return new Rect(position.x + width, position.y, Fix64.Max(0, position.width - width), position.height);
    }

    private static Rect PrepareVerticalVectorField(Rect position, string label)
    {
        var indent = indentLevel * 15;
        GUI.Label(new Rect(position.x + indent, position.y, Fix64.Max(0, position.width - indent),
            EditorGUIUtility.singleLineHeight), label);
        return new Rect(position.x + indent,
            position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing,
            Fix64.Max(0, position.width - indent), EditorGUIUtility.singleLineHeight);
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
        var fixedWidth = VectorAxisSpacing * (dimensions - 1);
        for (var axisIndex = 0; axisIndex < dimensions; axisIndex++)
            fixedWidth += GetVectorAxisLabelWidth(VectorAxisNames[axisIndex]) + 1;
        var numericWidth = Fix64.Max(0, (field.width - fixedWidth) / dimensions);
        var x = field.x;
        for (var axisIndex = 0; axisIndex < index; axisIndex++)
            x += GetVectorAxisLabelWidth(VectorAxisNames[axisIndex]) + 1 + numericWidth + VectorAxisSpacing;
        return new Rect(x, field.y,
            GetVectorAxisLabelWidth(VectorAxisNames[index]) + 1 + numericWidth, field.height);
    }

    private static float AxisFloatField(Rect position, string axis, Fix64 value)
    {
        var desiredWidth = GetVectorAxisLabelWidth(axis);
        var axisWidth = Fix64.Min(position.width, desiredWidth);
        GUI.Label(new Rect(position.x, position.y, axisWidth, position.height), axis,
            EditorStyles.vectorAxisLabel);
        var gap = position.width > axisWidth ? Fix64.One : Fix64.Zero;
        return FloatField(new Rect(position.x + axisWidth + gap, position.y,
            Fix64.Max(0, position.width - axisWidth - gap), position.height), (float)value);
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
        var hovered = field.Contains(Event.current.mousePosition);
        var lightTheme = EditorAppearance.theme == EditorTheme.Light;
        var border = GUIUtility.hotControl != 0 && hovered
            ? EditorAppearance.palette.FocusBorder
            : hovered ? ColorFromBytes(lightTheme ? 108 : 101, lightTheme ? 108 : 101,
                lightTheme ? 108 : 101) : ColorFromBytes(lightTheme ? 176 : 32,
                lightTheme ? 176 : 32, lightTheme ? 176 : 32);
        GUI.DrawRect(new Rect(field.x, field.y, field.width, 1), border);
        GUI.DrawRect(new Rect(field.x, field.yMax - 1, field.width, 1), border);
        GUI.DrawRect(new Rect(field.x, field.y, 1, field.height), border);
        GUI.DrawRect(new Rect(field.xMax - 1, field.y, 1, field.height), border);
    }

    private static void DrawEyedropperButton(Rect rect)
    {
        if (Event.current.type != EventType.Repaint || rect.width <= 0) return;
        var hovered = rect.Contains(Event.current.mousePosition);
        var lightTheme = EditorAppearance.theme == EditorTheme.Light;
        var background = lightTheme
            ? hovered ? ColorFromBytes(204, 204, 204) : ColorFromBytes(200, 200, 200)
            : hovered ? ColorFromBytes(76, 76, 76) : ColorFromBytes(56, 56, 56);
        GUI.DrawRect(rect, background);
        GUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), lightTheme
            ? ColorFromBytes(176, 176, 176) : ColorFromBytes(32, 32, 32));
        var tint = GUI.enabled ? EditorAppearance.palette.Text : EditorAppearance.palette.DisabledText;
        var startX = rect.x + (rect.width - 12) / 2;
        var startY = rect.y + (rect.height - 12) / 2;
        for (var index = 0; index < 6; index++)
            GUI.DrawRect(new Rect(startX + index, startY + 8 - index, 2, 2), tint);
        GUI.DrawRect(new Rect(startX + 7, startY + 1, 4, 4), tint);
        GUI.DrawRect(new Rect(startX + 1, startY + 9, 3, 2), tint);
    }

    private static Color ColorFromBytes(int red, int green, int blue, int alpha = 255) => new(
        Fix64.FromDecimal(red / 255m), Fix64.FromDecimal(green / 255m),
        Fix64.FromDecimal(blue / 255m), Fix64.FromDecimal(alpha / 255m));

    private static int ControlToken(int id, string label) => HashCode.Combine(id, label);

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
