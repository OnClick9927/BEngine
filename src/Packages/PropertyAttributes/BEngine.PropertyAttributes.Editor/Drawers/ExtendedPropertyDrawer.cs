using System.Collections;
using System.Globalization;
using System.Reflection;
using BEngine.Editor;

namespace BEngine.PropertyAttributes.Editor;

[CustomPropertyDrawer(typeof(ExtendedPropertyAttribute), useForChildren: true)]
public sealed class ExtendedPropertyDrawer : PropertyDrawer
{
    public override Fix64 GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var attributes = Attributes();
        if (!IsVisible(property, attributes)) return 0;
        var lines = 1 + attributes.OfType<InfoBoxAttribute>().Count() +
                    attributes.OfType<InlineButtonAttribute>().Count();
        if (attributes.Any(item => item is TitleAttribute)) lines++;
        return lines * (EditorGUIUtility.singleLineHeight + 2);
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        ArgumentNullException.ThrowIfNull(property);
        var attributes = Attributes();
        if (!IsVisible(property, attributes)) return;
        var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        if (attributes.OfType<TitleAttribute>().LastOrDefault() is { } title)
        {
            EditorGUI.LabelField(line, new GUIContent(string.IsNullOrWhiteSpace(title.subtitle) ? title.title :
                $"{title.title} - {title.subtitle}"), EditorStyles.boldLabel);
            line = Next(line);
        }
        foreach (var info in attributes.OfType<InfoBoxAttribute>())
        {
            if (info.visibleIf is null || EvaluateMember(property.serializedObject.targetObject, info.visibleIf, null, false))
            {
                EditorGUI.HelpBox(line, info.message, ToMessageType(info.messageType)); line = Next(line);
            }
        }
        var actualLabel = attributes.OfType<LabelTextAttribute>().LastOrDefault()?.label ?? label.text;
        var oldIndent = EditorGUI.indentLevel;
        EditorGUI.indentLevel += attributes.OfType<IndentAttribute>().LastOrDefault()?.level ?? 0;
        var oldEnabled = GUI.enabled;
        GUI.enabled &= IsEnabled(property, attributes) && attributes.All(item => item is not ReadOnlyAttribute);
        DrawField(line, property, new GUIContent(actualLabel, tooltip: property.tooltip), attributes);
        GUI.enabled = oldEnabled;
        EditorGUI.indentLevel = oldIndent;
        ApplyConstraints(property, attributes);
        line = Next(line);
        foreach (var button in attributes.OfType<InlineButtonAttribute>())
        {
            if (GUI.Button(EditorGUI.IndentedRect(line), button.label))
                InvokeAction(property.serializedObject.targetObject,
                button.methodName, property.boxedValue);
            line = Next(line);
        }
        foreach (var required in attributes.OfType<RequiredAttribute>().Where(_ => IsMissing(property.boxedValue)))
        {
            EditorGUI.HelpBox(line, required.message, MessageType.Error); line = Next(line);
        }
        foreach (var validation in attributes.OfType<ValidateInputAttribute>().Where(item =>
                     !InvokeValidator(property.serializedObject.targetObject, item.validatorMember, property.boxedValue)))
        {
            EditorGUI.HelpBox(line, validation.message, ToMessageType(validation.messageType)); line = Next(line);
        }
    }

    private void DrawField(Rect position, SerializedProperty property, GUIContent label,
        IReadOnlyList<ExtendedPropertyAttribute> attributes)
    {
        if (attributes.OfType<DropdownAttribute>().LastOrDefault() is { } dropdown)
        {
            var choices = dropdown.providerMember is { Length: > 0 }
                ? ResolveChoices(property.serializedObject.targetObject, dropdown.providerMember).ToArray()
                : dropdown.choices.ToArray();
            var index = property.propertyType == SerializedPropertyType.Enum ? property.enumValueIndex :
                Math.Max(0, Array.IndexOf(choices, property.stringValue));
            index = EditorGUI.Popup(position, label.text, index, choices);
            if (property.propertyType == SerializedPropertyType.Enum) property.enumValueIndex = index;
            else if (choices.Length > 0) property.stringValue = choices[index];
            return;
        }
        if (attributes.OfType<ProgressBarAttribute>().LastOrDefault() is { } progress)
        {
            position = EditorGUI.IndentedRect(position);
            var value = property.floatValue;
            GUI.DrawRect(position, new Color(Fix64.FromDecimal(0.12m), Fix64.FromDecimal(0.12m),
                Fix64.FromDecimal(0.12m), 1));
            var t = (value - progress.minimum) / Math.Max(0.0001f, progress.maximum - progress.minimum);
            GUI.DrawRect(new Rect(position.x, position.y, position.width * (Fix64)Math.Clamp(t, 0, 1), position.height),
                new Color(Fix64.FromDecimal(0.18m), Fix64.FromDecimal(0.52m), Fix64.FromDecimal(0.72m), 1));
            GUI.Label(position, progress.title ?? $"{label.text}: {value:0.##}");
            if (progress.editable) property.floatValue = (float)GUI.HorizontalSlider(position, (Fix64)value,
                (Fix64)progress.minimum, (Fix64)progress.maximum);
            return;
        }
        if (attributes.OfType<MinMaxAttribute>().LastOrDefault() is { } minMax &&
            property.propertyType == SerializedPropertyType.Vector2)
        {
            var value = property.vector2Value;
            var updated = EditorGUI.Vector2Field(position, label.text, value);
            property.vector2Value = new Vector2(Fix64.Clamp(updated.x, (Fix64)minMax.minimum, updated.y),
                Fix64.Clamp(updated.y, updated.x, (Fix64)minMax.maximum));
            return;
        }
        if (attributes.OfType<PasswordAttribute>().LastOrDefault() is { } password &&
            property.propertyType == SerializedPropertyType.String)
        {
            property.stringValue = GUI.PasswordField(EditorGUI.PrefixLabel(position, label), property.stringValue,
                password.mask); return;
        }
        if (attributes.Any(item => item is FilePathAttribute or FolderPathAttribute or AssetPathAttribute) &&
            property.propertyType == SerializedPropertyType.String)
        {
            var field = EditorGUI.PrefixLabel(position, label);
            property.stringValue = GUI.TextField(new Rect(field.x, field.y, Fix64.Max(0, field.width - 28), field.height),
                property.stringValue);
            if (EditorToolbar.Button(new Rect(field.xMax - 26, field.y, 26, field.height),
                    new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.Browse, "Browse")))
                SelectPath(property, attributes);
            return;
        }
        if (EditorReflectionCache.GetAttributes(memberInfo).OfType<RangeAttribute>().FirstOrDefault() is { } range &&
            property.propertyType is SerializedPropertyType.Integer or SerializedPropertyType.Float)
        {
            if (property.propertyType == SerializedPropertyType.Integer)
                property.intValue = EditorGUI.IntSlider(position, label.text, property.intValue,
                    (int)MathF.Ceiling(range.min), (int)MathF.Floor(range.max));
            else property.floatValue = EditorGUI.Slider(position, label.text, property.floatValue,
                range.min, range.max);
            return;
        }
        EditorGUI.DefaultPropertyField(position, property, label, true);
    }

    private ExtendedPropertyAttribute[] Attributes() => EditorReflectionCache.GetAttributes(memberInfo)
        .OfType<ExtendedPropertyAttribute>().OrderBy(item => item.order).ToArray();
    private static bool IsVisible(SerializedProperty property, IEnumerable<ExtendedPropertyAttribute> attributes) =>
        attributes.OfType<ShowIfAttribute>().All(item => EvaluateCondition(property.serializedObject.targetObject, item)) &&
        attributes.OfType<HideIfAttribute>().All(item => !EvaluateCondition(property.serializedObject.targetObject, item));
    private static bool IsEnabled(SerializedProperty property, IEnumerable<ExtendedPropertyAttribute> attributes) =>
        attributes.OfType<EnableIfAttribute>().All(item => EvaluateCondition(property.serializedObject.targetObject, item)) &&
        attributes.OfType<DisableIfAttribute>().All(item => !EvaluateCondition(property.serializedObject.targetObject, item));
    private static bool EvaluateCondition(object target, ConditionalPropertyAttribute condition) =>
        EvaluateMember(target, condition.conditionMember, condition.expectedValue, condition.hasExpectedValue);
    private static bool EvaluateMember(object target, string name, object? expected, bool compare)
    {
        if (!TryGetMemberValue(target, name, out var value)) return false;
        if (!compare) return value switch { bool boolean => boolean, null => false, string text => text.Length > 0, _ => true };
        if (value is null) return expected is null;
        try
        {
            var converted = expected is null ? null : value.GetType().IsEnum
                ? expected is string text ? Enum.Parse(value.GetType(), text, true) : Enum.ToObject(value.GetType(), expected)
                : Convert.ChangeType(expected, value.GetType(), CultureInfo.InvariantCulture);
            return Equals(value, converted);
        }
        catch { return Equals(value, expected); }
    }
    private static bool TryGetMemberValue(object target, string name, out object? value)
    {
        return EditorReflectionCache.TryGetMemberValue(target, name, out value);
    }
    private static void ApplyConstraints(SerializedProperty property, IEnumerable<ExtendedPropertyAttribute> attributes)
    {
        if (property.propertyType is not (SerializedPropertyType.Integer or SerializedPropertyType.Float)) return;
        var minimum = attributes.OfType<ClampAttribute>().Select(item => item.minimum).DefaultIfEmpty(double.NegativeInfinity).Max();
        var maximum = attributes.OfType<ClampAttribute>().Select(item => item.maximum)
            .Concat(attributes.OfType<MaxValueAttribute>().Select(item => item.maximum)).DefaultIfEmpty(double.PositiveInfinity).Min();
        var constrained = Math.Clamp(property.doubleValue, minimum, maximum);
        if (Math.Abs(constrained - property.doubleValue) > double.Epsilon) property.doubleValue = constrained;
    }
    private static IReadOnlyList<string> ResolveChoices(object target, string providerMember) =>
        TryGetMemberValue(target, providerMember, out var value) && value is IEnumerable values
            ? values.Cast<object?>().Select(item => item?.ToString() ?? string.Empty).ToArray() : [];
    private static bool InvokeValidator(object target, string name, object? value)
    {
        return EditorReflectionCache.TryInvokePredicate(target, name, value, out var result) && result;
    }
    private static void InvokeAction(object target, string name, object? value)
    {
        EditorReflectionCache.TryInvokeAction(target, name, value);
    }
    private static bool IsMissing(object? value) => value switch
    { null => true, string text => string.IsNullOrWhiteSpace(text), BObject engineObject => engineObject == null, _ => false };
    private static MessageType ToMessageType(ValidationMessageType type) => type switch
    { ValidationMessageType.Warning => MessageType.Warning, ValidationMessageType.Error => MessageType.Error, _ => MessageType.Info };
    private static Rect Next(Rect line) => new(line.x, line.y + EditorGUIUtility.singleLineHeight + 2,
        line.width, line.height);
    private static void SelectPath(SerializedProperty property, IReadOnlyList<ExtendedPropertyAttribute> attributes)
    {
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
        void Accept(string path, bool relative) => property.stringValue = relative
            ? Path.GetRelativePath(projectRoot, path).Replace('\\', '/') : Path.GetFullPath(path);
        if (attributes.OfType<FilePathAttribute>().LastOrDefault() is { } file)
            EditorFileDialog.Open(file.title, projectRoot, file.filter, path => Accept(path, file.relativeToProject));
        else if (attributes.OfType<FolderPathAttribute>().LastOrDefault() is { } folder)
            EditorFileDialog.OpenFolder(folder.description, projectRoot, path => Accept(path, folder.relativeToProject));
        else if (attributes.OfType<AssetPathAttribute>().LastOrDefault() is { } asset)
        {
            var filter = string.IsNullOrWhiteSpace(asset.extension) ? "All assets (*.*)|*.*" :
                $"Assets (*.{asset.extension!.TrimStart('.')})|*.{asset.extension.TrimStart('.')}";
            EditorFileDialog.Open("Select Asset", Application.dataPath, filter, path => Accept(path, true));
        }
    }
}
