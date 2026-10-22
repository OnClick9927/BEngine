using System.Collections;
using System.Globalization;
using System.Reflection;
using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.Editor;

[CustomPropertyDrawer(typeof(ExtendedPropertyAttribute), useForChildren: true)]
public sealed class ExtendedPropertyDrawer : PropertyDrawer
{
    public override Fix64 GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var attributes = Attributes();
        if (!IsVisible(property, attributes)) return 0;
        var target = property.serializedObject.targetObject;
        var spacing = EditorGUIUtility.standardVerticalSpacing;
        var height = EditorGUIUtility.singleLineHeight + spacing;
        height += (Fix64)attributes.OfType<HorizontalLineAttribute>()
            .Sum(item => item.thickness + 4);
        if (attributes.Any(item => item is TitleAttribute))
            height += EditorGUIUtility.singleLineHeight + spacing;
        var width = EstimatedHelpBoxWidth();
        foreach (var info in attributes.OfType<InfoBoxAttribute>().Where(item =>
                     item.visibleIf is null || EvaluateMember(property, item.visibleIf, null, false)))
            height += HelpBoxHeight(info.message, ToMessageType(info.messageType), width) + spacing;
        height += attributes.OfType<InlineButtonAttribute>().Count() *
                  (EditorGUIUtility.singleLineHeight + spacing);
        foreach (var required in attributes.OfType<RequiredAttribute>().Where(_ => IsMissing(property.boxedValue)))
            height += HelpBoxHeight(required.message, MessageType.Error, width) + spacing;
        foreach (var validation in attributes.OfType<ValidateInputAttribute>().Where(item =>
                     !InvokeValidator(target, item.validatorMember, property.boxedValue)))
            height += HelpBoxHeight(validation.message, ToMessageType(validation.messageType), width) + spacing;
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        ArgumentNullException.ThrowIfNull(property);
        var attributes = Attributes();
        if (!IsVisible(property, attributes)) return;
        var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        foreach (var rule in attributes.OfType<HorizontalLineAttribute>())
        {
            var color = new Color(Fix64.FromDecimal(0.33m), Fix64.FromDecimal(0.35m), Fix64.FromDecimal(0.37m), 1);
            if (rule.color.Length == 7 && rule.color[0] == '#' &&
                uint.TryParse(rule.color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                color = new Color((Fix64)((rgb >> 16) & 255) / 255, (Fix64)((rgb >> 8) & 255) / 255,
                    (Fix64)(rgb & 255) / 255, 1);
            GUI.DrawRect(new Rect(line.x, line.y + 2, line.width, (Fix64)rule.thickness), color);
            line = new Rect(line.x, line.y + (Fix64)rule.thickness + 4, line.width, line.height);
        }
        if (attributes.OfType<TitleAttribute>().LastOrDefault() is { } title)
        {
            EditorGUI.LabelField(line, new GUIContent(string.IsNullOrWhiteSpace(title.subtitle) ? title.title :
                $"{title.title} - {title.subtitle}"), EditorStyles.boldLabel);
            line = Next(line);
        }
        foreach (var info in attributes.OfType<InfoBoxAttribute>())
        {
            if (info.visibleIf is null || EvaluateMember(property, info.visibleIf, null, false))
            {
                var type = ToMessageType(info.messageType);
                line = DrawHelpBox(line, info.message, type);
            }
        }
        var actualLabel = attributes.OfType<LabelTextAttribute>().LastOrDefault()?.label ?? label.text;
        var oldIndent = EditorGUI.indentLevel;
        EditorGUI.indentLevel += attributes.OfType<IndentAttribute>().LastOrDefault()?.level ?? 0;
        var oldEnabled = GUI.enabled;
        GUI.enabled &= IsEnabled(property, attributes) && attributes.All(item => item is not ReadOnlyAttribute);
        try
        {
            var field = line;
            var fieldLabel = new GUIContent(actualLabel, tooltip: property.tooltip);
            var prefix = attributes.OfType<PrefixAttribute>().LastOrDefault();
            var suffix = attributes.OfType<SuffixAttribute>().LastOrDefault();
            if (prefix is not null || suffix is not null)
            {
                field = EditorGUI.PrefixLabel(field, fieldLabel);
                fieldLabel = GUIContent.none;
                if (prefix is not null)
                {
                    var width = Fix64.Min(field.width / 3, EditorStyles.label.CalcSize(new GUIContent(prefix.text)).x + 4);
                    GUI.Label(new Rect(field.x, field.y, width, field.height), prefix.text);
                    field = new Rect(field.x + width, field.y, field.width - width, field.height);
                }
                if (suffix is not null)
                {
                    var width = Fix64.Min(field.width / 3, EditorStyles.label.CalcSize(new GUIContent(suffix.text)).x + 4);
                    GUI.Label(new Rect(field.xMax - width, field.y, width, field.height), suffix.text);
                    field = new Rect(field.x, field.y, field.width - width, field.height);
                }
            }
            DrawField(field, property, fieldLabel, attributes);
        }
        finally
        {
            GUI.enabled = oldEnabled;
            EditorGUI.indentLevel = oldIndent;
        }
        ApplyConstraints(property, attributes);
        line = Next(line);
        foreach (var button in attributes.OfType<InlineButtonAttribute>())
        {
            if (GUI.Button(EditorGUI.IndentedRect(line), button.label))
            {
                property.serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(property.serializedObject.targetObject, button.label);
                InvokeAction(property.serializedObject.targetObject,
                button.methodName, property.boxedValue);
                property.serializedObject.Update();
            }
            line = Next(line);
        }
        foreach (var required in attributes.OfType<RequiredAttribute>().Where(_ => IsMissing(property.boxedValue)))
        {
            line = DrawHelpBox(line, required.message, MessageType.Error);
        }
        foreach (var validation in attributes.OfType<ValidateInputAttribute>().Where(item =>
                     !InvokeValidator(property.serializedObject.targetObject, item.validatorMember, property.boxedValue)))
        {
            line = DrawHelpBox(line, validation.message, ToMessageType(validation.messageType));
        }
    }

    private void DrawField(Rect position, SerializedProperty property, GUIContent label,
        IReadOnlyList<ExtendedPropertyAttribute> attributes)
    {
        if (attributes.Any(item => item is EnumFlagsAttribute) && property.boxedValue is Enum flags)
        {
            var field = EditorGUI.PrefixLabel(position, label);
            if (EditorGUI.DropdownButton(field, flags.ToString()))
            {
                var menu = new GenericMenu();
                var enumType = flags.GetType();
                var value = EnumBits(flags);
                foreach (Enum option in Enum.GetValues(enumType))
                {
                    var bits = EnumBits(option);
                    var selected = bits == 0 ? value == 0 : (value & bits) == bits;
                    menu.AddItem(new GUIContent(option.ToString()), selected, () =>
                    {
                        property.boxedValue = Enum.ToObject(enumType, bits == 0 ? 0UL :
                            selected ? value & ~bits : value | bits);
                        property.serializedObject.ApplyModifiedProperties();
                    });
                }
                menu.DropDown(field);
            }
            return;
        }
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
            var minimum = (Fix64)Math.Min(minMax.minimum, minMax.maximum);
            var maximum = (Fix64)Math.Max(minMax.minimum, minMax.maximum);
            var low = Fix64.Clamp(updated.x, minimum, maximum);
            property.vector2Value = new Vector2(low, Fix64.Clamp(updated.y, low, maximum));
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
    private static ulong EnumBits(Enum value) => Type.GetTypeCode(Enum.GetUnderlyingType(value.GetType())) is
        TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64
            ? Convert.ToUInt64(value) : unchecked((ulong)Convert.ToInt64(value));
    private static bool IsVisible(SerializedProperty property, IEnumerable<ExtendedPropertyAttribute> attributes) =>
        attributes.OfType<ShowIfAttribute>().All(item => EvaluateCondition(property, item)) &&
        attributes.OfType<HideIfAttribute>().All(item => !EvaluateCondition(property, item));
    private static bool IsEnabled(SerializedProperty property, IEnumerable<ExtendedPropertyAttribute> attributes) =>
        attributes.OfType<EnableIfAttribute>().All(item => EvaluateCondition(property, item)) &&
        attributes.OfType<DisableIfAttribute>().All(item => !EvaluateCondition(property, item));
    private static bool EvaluateCondition(SerializedProperty property, ConditionalPropertyAttribute condition) =>
        EvaluateMember(property, condition.conditionMember, condition.expectedValue, condition.hasExpectedValue);
    private static bool EvaluateMember(SerializedProperty property, string name, object? expected, bool compare)
    {
        object? value;
        var condition = property.serializedObject.FindProperty(name);
        if (condition is not null)
        {
            if (!condition.TryGetBoxedValue(out value)) return false;
        }
        else if (!TryGetMemberValue(property.serializedObject.targetObject, name, out value)) return false;
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
    private static Rect DrawHelpBox(Rect line, string message, MessageType type)
    {
        var height = HelpBoxHeight(message, type, line.width);
        EditorGUI.HelpBox(new Rect(line.x, line.y, line.width, height), message, type);
        return new Rect(line.x, line.y + height + EditorGUIUtility.standardVerticalSpacing,
            line.width, EditorGUIUtility.singleLineHeight);
    }
    private static Fix64 HelpBoxHeight(string message, MessageType type, Fix64 width) =>
        EditorGUI.GetHelpBoxHeight(message, type, width);
    private static Fix64 EstimatedHelpBoxWidth() =>
        Fix64.Max(1, EditorGUIUtility.currentViewWidth - 36);
    private static void SelectPath(SerializedProperty property, IReadOnlyList<ExtendedPropertyAttribute> attributes)
    {
        var paths = EditorServices.GetRequiredService<PathUtility>();
        var platform = EditorServices.GetRequiredService<PlatformUtility>();
        var projectRoot = paths.GetDirectoryName(paths.GetFullPath(Application.dataPath)) ?? platform.CurrentDirectory;
        void Accept(string path, bool relative) => property.stringValue = relative
            ? paths.GetRelativePath(projectRoot, path).Replace('\\', '/') : paths.GetFullPath(path);
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
