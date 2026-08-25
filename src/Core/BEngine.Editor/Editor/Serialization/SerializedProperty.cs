using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

public sealed class SerializedProperty : IDisposable
{
    private readonly SerializedObject _serializedObject;
    private readonly string[]? _iteratorPaths;
    private int _iteratorIndex = -1;
    private string _propertyPath;
    private string _name;
    private MemberInfo? _memberInfo;
    private SerializedMemberMetadata _metadata;
    private Type _valueType;
    private bool _editable;
    private string _displayName = string.Empty;
    private int _depth;

    public SerializedObject serializedObject => _serializedObject;
    public string propertyPath => _propertyPath;
    public string name => _name;
    public string displayName => _displayName;
    public string tooltip => _metadata.Tooltip;
    public string type => valueType.Name;
    public Type valueType => _valueType;
    public MemberInfo? memberInfo => _memberInfo;
    public int depth => _depth;
    public bool editable => _editable;
    public bool hasMultipleDifferentValues
    {
        get
        {
            if (_serializedObject.targetObjects.Length < 2) return false;
            var first = PropertyPath.Resolve(_serializedObject.targetObjects[0], propertyPath).GetValue();
            for (var index = 1; index < _serializedObject.targetObjects.Length; index++)
                if (!Equals(first, PropertyPath.Resolve(_serializedObject.targetObjects[index], propertyPath).GetValue()))
                    return true;
            return false;
        }
    }
    public bool isArray => boxedValue is IList or Array;
    public bool hasVisibleChildren => isArray && arraySize > 0;
    public bool isExpanded { get; set; }
    public SerializedPropertyType propertyType => GetPropertyType(valueType);

    internal SerializedProperty(SerializedObject serializedObject, string propertyPath)
    {
        _serializedObject = serializedObject;
        _propertyPath = propertyPath;
        _name = string.Empty;
        _metadata = SerializedMemberMetadata.For(null);
        _valueType = typeof(object);
        RefreshMetadata();
    }

    private SerializedProperty(SerializedObject serializedObject, string[] iteratorPaths)
    {
        _serializedObject = serializedObject;
        _iteratorPaths = iteratorPaths;
        _propertyPath = string.Empty;
        _name = string.Empty;
        _metadata = SerializedMemberMetadata.For(null);
        _valueType = typeof(object);
    }

    internal static SerializedProperty CreateIterator(SerializedObject serializedObject, string[] paths) =>
        new(serializedObject, paths);

    public object? boxedValue
    {
        get => _serializedObject.GetValue(propertyPath);
        set => _serializedObject.SetValue(propertyPath, value);
    }

    public int intValue
    {
        get => Convert.ToInt32(boxedValue, System.Globalization.CultureInfo.InvariantCulture);
        set => boxedValue = ConvertNumeric(value);
    }

    public long longValue
    {
        get => Convert.ToInt64(boxedValue, System.Globalization.CultureInfo.InvariantCulture);
        set => boxedValue = ConvertNumeric(value);
    }

    public bool boolValue { get => boxedValue is true; set => boxedValue = value; }
    public string stringValue { get => boxedValue?.ToString() ?? string.Empty; set => boxedValue = value; }

    public float floatValue
    {
        get => boxedValue is Fix64 fixedValue ? (float)fixedValue : Convert.ToSingle(boxedValue);
        set => boxedValue = PropertyPath.Resolve(_serializedObject.targetObject, propertyPath).ValueType == typeof(Fix64)
            ? (Fix64)value
            : ConvertNumeric(value);
    }

    public double doubleValue
    {
        get => boxedValue is Fix64 fixedValue ? (double)fixedValue : Convert.ToDouble(boxedValue);
        set => boxedValue = PropertyPath.Resolve(_serializedObject.targetObject, propertyPath).ValueType == typeof(Fix64)
            ? (Fix64)value
            : ConvertNumeric(value);
    }

    public Vector2 vector2Value { get => boxedValue is Vector2 value ? value : Vector2.zero; set => boxedValue = value; }
    public Vector4 vector4Value { get => boxedValue is Vector4 value ? value : Vector4.zero; set => boxedValue = value; }
    public Color colorValue { get => boxedValue is Color value ? value : Color.white; set => boxedValue = value; }
    public Rect rectValue { get => boxedValue is Rect value ? value : default; set => boxedValue = value; }
    public BObject? objectReferenceValue
    {
        get => boxedValue as BObject;
        set
        {
            if (value is not null && !valueType.IsInstanceOfType(value))
                throw new ArgumentException(
                    $"Object of type {value.GetType().Name} cannot be assigned to {valueType.Name}.",
                    nameof(value));
            boxedValue = value;
        }
    }
    public int objectReferenceInstanceIDValue => objectReferenceValue?.GetInstanceID() ?? 0;

    public int enumValueIndex
    {
        get
        {
            if (boxedValue is not Enum value) return intValue;
            var values = Enum.GetValues(value.GetType());
            for (var index = 0; index < values.Length; index++)
                if (Equals(values.GetValue(index), value)) return index;
            return 0;
        }
        set
        {
            var valueType = PropertyPath.Resolve(_serializedObject.targetObject, propertyPath).ValueType;
            if (!valueType.IsEnum) { boxedValue = ConvertNumeric(value); return; }
            var values = Enum.GetValues(valueType);
            boxedValue = values.GetValue(Math.Clamp(value, 0, Math.Max(0, values.Length - 1)));
        }
    }

    public string[] enumDisplayNames => PropertyPath.Resolve(_serializedObject.targetObject, propertyPath)
        .ValueType is { IsEnum: true } enumType ? Enum.GetNames(enumType) : [];

    public int arraySize
    {
        get => boxedValue switch { Array array => array.Length, IList list => list.Count, _ => 0 };
        set
        {
            var count = Math.Max(0, value);
            if (boxedValue is IList list && !list.IsFixedSize)
            {
                while (list.Count > count) list.RemoveAt(list.Count - 1);
                var elementType = list.GetType().IsGenericType ? list.GetType().GetGenericArguments()[0] : typeof(object);
                while (list.Count < count) list.Add(elementType.IsValueType ? Activator.CreateInstance(elementType) : null);
                boxedValue = list;
                return;
            }
            if (boxedValue is Array array)
            {
                var resized = Array.CreateInstance(array.GetType().GetElementType()!, count);
                Array.Copy(array, resized, Math.Min(array.Length, count));
                boxedValue = resized;
            }
        }
    }

    public SerializedProperty? FindPropertyRelative(string relativePropertyPath) =>
        _serializedObject.FindProperty($"{propertyPath}.{relativePropertyPath}");

    public bool Next(bool enterChildren) => MoveNext(visibleOnly: false);
    public bool NextVisible(bool enterChildren) => MoveNext(visibleOnly: true);

    public SerializedProperty GetArrayElementAtIndex(int index)
    {
        if (index < 0 || index >= arraySize) throw new ArgumentOutOfRangeException(nameof(index));
        return new SerializedProperty(_serializedObject, $"{propertyPath}[{index}]");
    }

    public void InsertArrayElementAtIndex(int index)
    {
        if (boxedValue is not IList list || list.IsFixedSize)
            throw new InvalidOperationException($"{propertyPath} is not a resizable list.");
        var elementType = list.GetType().IsGenericType ? list.GetType().GetGenericArguments()[0] : typeof(object);
        var value = index > 0 && index <= list.Count ? list[index - 1] :
            elementType.IsValueType ? Activator.CreateInstance(elementType) : null;
        list.Insert(Math.Clamp(index, 0, list.Count), value);
        boxedValue = list;
    }

    public void DeleteArrayElementAtIndex(int index)
    {
        if (boxedValue is not IList list || list.IsFixedSize || index < 0 || index >= list.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        list.RemoveAt(index);
        boxedValue = list;
    }

    public bool MoveArrayElement(int sourceIndex, int destinationIndex)
    {
        if (boxedValue is not IList list || list.IsFixedSize || sourceIndex < 0 || sourceIndex >= list.Count ||
            destinationIndex < 0 || destinationIndex >= list.Count) return false;
        var value = list[sourceIndex];
        list.RemoveAt(sourceIndex);
        list.Insert(destinationIndex, value);
        boxedValue = list;
        return true;
    }

    public void ClearArray()
    {
        if (boxedValue is not IList list || list.IsFixedSize)
            throw new InvalidOperationException($"{propertyPath} is not a resizable list.");
        list.Clear();
        boxedValue = list;
    }

    public SerializedProperty Copy() => new(_serializedObject, propertyPath) { isExpanded = isExpanded };
    public void Dispose() { }

    private object ConvertNumeric(object value)
    {
        var valueType = PropertyPath.Resolve(_serializedObject.targetObject, propertyPath).ValueType;
        return Convert.ChangeType(value, valueType, System.Globalization.CultureInfo.InvariantCulture);
    }

    internal T? GetAttribute<T>() where T : Attribute =>
        SerializedMemberMetadata.For(memberInfo).Attributes.OfType<T>().FirstOrDefault();

    internal Attribute[] GetAttributes() => SerializedMemberMetadata.For(memberInfo).Attributes;

    private bool MoveNext(bool visibleOnly)
    {
        if (_iteratorPaths is null) return false;
        while (++_iteratorIndex < _iteratorPaths.Length)
        {
            var path = _iteratorPaths[_iteratorIndex];
            if (visibleOnly && !_serializedObject.IsVisible(path)) continue;
            _propertyPath = path;
            RefreshMetadata();
            return true;
        }
        _propertyPath = string.Empty;
        return false;
    }

    private void RefreshMetadata()
    {
        _name = PropertyPath.LeafName(_propertyPath);
        var accessor = PropertyPath.Resolve(_serializedObject.targetObject, _propertyPath);
        _memberInfo = accessor.MemberInfo;
        _metadata = SerializedMemberMetadata.For(_memberInfo);
        _valueType = accessor.ValueType;
        _editable = accessor.CanWrite;
        _displayName = _metadata.DisplayName ?? ObjectNames.NicifyVariableName(_name);
        _depth = 0;
        foreach (var character in _propertyPath) if (character == '.') _depth++;
    }

    internal PropertyAttribute[] GetPropertyAttributes() => _metadata.PropertyAttributes;

    private static SerializedPropertyType GetPropertyType(Type type)
    {
        if (type.IsEnum) return SerializedPropertyType.Enum;
        if (type == typeof(bool)) return SerializedPropertyType.Boolean;
        if (type == typeof(string)) return SerializedPropertyType.String;
        if (type == typeof(Fix64) || type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return SerializedPropertyType.Float;
        if (type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long) ||
            type == typeof(uint) || type == typeof(ulong)) return SerializedPropertyType.Integer;
        if (type == typeof(Vector2)) return SerializedPropertyType.Vector2;
        if (type == typeof(Vector4)) return SerializedPropertyType.Vector4;
        if (type == typeof(Color)) return SerializedPropertyType.Color;
        if (type == typeof(Rect)) return SerializedPropertyType.Rect;
        if (typeof(BObject).IsAssignableFrom(type)) return SerializedPropertyType.ObjectReference;
        return SerializedPropertyType.Generic;
    }
}
