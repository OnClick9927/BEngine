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
            if (!_serializedObject.TryGetValues(propertyPath, out var values)) return false;
            var first = values[0];
            for (var index = 1; index < values.Length; index++)
                if (!Equals(first, values[index]))
                    return true;
            return false;
        }
    }
    public bool isArray => IsSupportedCollectionType(valueType);
    public bool hasVisibleChildren => _serializedObject.GetVisibleChildren(this).Count > 0;
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
        get
        {
            if (!_serializedObject.TryGetValues(propertyPath, out var values)) return 0;
            var minimum = int.MaxValue;
            foreach (var value in values)
            {
                if (value is null)
                {
                    minimum = 0;
                    continue;
                }
                if (value is not IList list) return 0;
                minimum = Math.Min(minimum, list.Count);
            }
            return minimum == int.MaxValue ? 0 : minimum;
        }
        set
        {
            var count = Math.Max(0, value);
            var values = GetCollectionValues();
            var resized = values.Select(value => ResizeCollection(value, valueType, count)).ToArray();
            _serializedObject.SetValues(propertyPath, resized);
        }
    }

    public SerializedProperty? FindPropertyRelative(string relativePropertyPath) =>
        _serializedObject.FindProperty($"{propertyPath}.{relativePropertyPath}");

    public bool Next(bool enterChildren) => MoveNext(visibleOnly: false);
    public bool NextVisible(bool enterChildren) => MoveNext(visibleOnly: true);

    public SerializedProperty GetArrayElementAtIndex(int index)
    {
        if (index < 0 || index >= arraySize) throw new ArgumentOutOfRangeException(nameof(index));
        return _serializedObject.GetOrCreateProperty($"{propertyPath}[{index}]");
    }

    public void InsertArrayElementAtIndex(int index)
    {
        var values = GetCollectionValues();
        var inserted = values.Select(value => InsertCollectionElement(value, valueType, index)).ToArray();
        _serializedObject.SetValues(propertyPath, inserted);
    }

    public void DeleteArrayElementAtIndex(int index)
    {
        if (index < 0 || index >= arraySize) throw new ArgumentOutOfRangeException(nameof(index));
        var values = GetCollectionValues();
        var deleted = values.Select(value => DeleteCollectionElement(value!, valueType, index)).ToArray();
        _serializedObject.SetValues(propertyPath, deleted);
    }

    public bool MoveArrayElement(int sourceIndex, int destinationIndex)
    {
        var count = arraySize;
        if (sourceIndex < 0 || sourceIndex >= count || destinationIndex < 0 || destinationIndex >= count)
            return false;
        if (sourceIndex == destinationIndex) return true;
        var values = GetCollectionValues();
        if (values.Any(static value => value is not IList list || list.IsReadOnly ||
                                       list.IsFixedSize && value is not Array)) return false;
        var reordered = values
            .Select(value => MoveCollectionElement((IList)value!, valueType, sourceIndex, destinationIndex))
            .ToArray();
        _serializedObject.SetValues(propertyPath, reordered);
        return true;
    }

    public void ClearArray()
    {
        var values = GetCollectionValues();
        var cleared = values.Select(value => ResizeCollection(value, valueType, 0)).ToArray();
        _serializedObject.SetValues(propertyPath, cleared);
    }

    public SerializedProperty Copy() => new(_serializedObject, propertyPath) { isExpanded = isExpanded };
    public void Dispose() { }

    internal IReadOnlyList<SerializedProperty> GetVisibleChildren() =>
        _serializedObject.GetVisibleChildren(this);

    internal bool TryGetBoxedValue(out object? value) =>
        _serializedObject.TryGetValue(propertyPath, out value);

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
        _displayName = _metadata.DisplayName ?? GetDisplayName(_propertyPath, _name);
        _depth = 0;
        foreach (var character in _propertyPath)
            if (character is '.' or '[') _depth++;
    }

    internal PropertyAttribute[] GetPropertyAttributes() => _metadata.PropertyAttributes;

    private object?[] GetCollectionValues()
    {
        if (!isArray || !_serializedObject.TryGetValues(propertyPath, out var values))
            throw new InvalidOperationException($"{propertyPath} is not an array or List.");
        if (values.Any(static value => value is not null and not IList))
            throw new InvalidOperationException($"{propertyPath} contains an unsupported collection value.");
        return values;
    }

    private static object? ResizeCollection(object? value, Type declaredType, int count)
    {
        var elementType = ElementType(declaredType);
        if (declaredType.IsArray || value is Array)
        {
            var source = value as Array;
            if (source is not null && source.Rank != 1)
                throw new NotSupportedException("Only one-dimensional arrays can be edited by the Inspector.");
            if (source?.Length == count) return source;
            var resized = Array.CreateInstance(source?.GetType().GetElementType() ?? elementType, count);
            if (source is not null) Array.Copy(source, resized, Math.Min(source.Length, count));
            return resized;
        }

        if (value is IList list && list.Count == count) return list;
        var resizedList = value is IList existing
            ? CloneResizableList(existing)
            : CreateResizableList(declaredType, elementType);
        while (resizedList.Count > count) resizedList.RemoveAt(resizedList.Count - 1);
        while (resizedList.Count < count) resizedList.Add(DefaultValue(elementType));
        return resizedList;
    }

    private static object InsertCollectionElement(object? value, Type declaredType, int requestedIndex)
    {
        var elementType = ElementType(declaredType);
        if (declaredType.IsArray || value is Array)
        {
            var source = value as Array;
            if (source is not null && source.Rank != 1)
                throw new NotSupportedException("Only one-dimensional arrays can be edited by the Inspector.");
            var length = source?.Length ?? 0;
            var insertion = Math.Clamp(requestedIndex, 0, length);
            var resized = Array.CreateInstance(source?.GetType().GetElementType() ?? elementType, length + 1);
            if (source is not null)
            {
                if (insertion > 0) Array.Copy(source, 0, resized, 0, insertion);
                if (insertion < length) Array.Copy(source, insertion, resized, insertion + 1, length - insertion);
            }
            resized.SetValue(insertion > 0 && source is not null
                ? source.GetValue(insertion - 1)
                : DefaultValue(elementType), insertion);
            return resized;
        }

        var sourceList = value as IList;
        var resizedList = sourceList is null
            ? CreateResizableList(declaredType, elementType)
            : CloneResizableList(sourceList);
        var insertionIndex = Math.Clamp(requestedIndex, 0, resizedList.Count);
        var insertedValue = insertionIndex > 0
            ? resizedList[insertionIndex - 1]
            : DefaultValue(elementType);
        resizedList.Insert(insertionIndex, insertedValue);
        return resizedList;
    }

    private static object DeleteCollectionElement(object value, Type declaredType, int index)
    {
        if (value is Array array)
        {
            if (array.Rank != 1) throw new NotSupportedException(
                "Only one-dimensional arrays can be edited by the Inspector.");
            var resized = Array.CreateInstance(array.GetType().GetElementType() ?? ElementType(declaredType),
                array.Length - 1);
            if (index > 0) Array.Copy(array, 0, resized, 0, index);
            if (index + 1 < array.Length)
                Array.Copy(array, index + 1, resized, index, array.Length - index - 1);
            return resized;
        }
        if (value is not IList list || list.IsFixedSize || list.IsReadOnly)
            throw new InvalidOperationException($"{declaredType.Name} cannot be resized by the Inspector.");
        var resizedList = CloneResizableList(list);
        resizedList.RemoveAt(index);
        return resizedList;
    }

    private static object MoveCollectionElement(IList value, Type declaredType, int sourceIndex,
        int destinationIndex)
    {
        if (value is Array array)
        {
            var reordered = (Array)array.Clone();
            MoveFixedSizeElement(reordered, sourceIndex, destinationIndex);
            return reordered;
        }
        var reorderedList = CloneResizableList(value);
        var element = reorderedList[sourceIndex];
        reorderedList.RemoveAt(sourceIndex);
        reorderedList.Insert(destinationIndex, element);
        return reorderedList;
    }

    private static IList CloneResizableList(IList source)
    {
        var result = CreateResizableList(source.GetType(), ElementType(source.GetType()));
        foreach (var value in source) result.Add(value);
        return result;
    }

    private static IList CreateResizableList(Type collectionType, Type elementType)
    {
        if (!collectionType.IsInterface && !collectionType.IsAbstract)
        {
            try
            {
                if (Activator.CreateInstance(collectionType, nonPublic: true) is IList created &&
                    !created.IsFixedSize && !created.IsReadOnly) return created;
            }
            catch (MissingMethodException) { }
        }

        var fallbackType = typeof(List<>).MakeGenericType(elementType);
        if (Activator.CreateInstance(fallbackType) is IList fallback) return fallback;
        throw new InvalidOperationException($"{collectionType.Name} cannot be resized by the Inspector.");
    }

    private static bool IsSupportedCollectionType(Type type)
    {
        if (type.IsArray || typeof(IList).IsAssignableFrom(type)) return true;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>)) return true;
        return type.GetInterfaces().Any(candidate => candidate.IsGenericType &&
            candidate.GetGenericTypeDefinition() == typeof(IList<>));
    }

    private static Type ElementType(Type collectionType)
    {
        if (collectionType.IsArray) return collectionType.GetElementType() ?? typeof(object);
        if (collectionType.IsGenericType && collectionType.GetGenericArguments().Length == 1)
            return collectionType.GetGenericArguments()[0];
        var genericList = collectionType.GetInterfaces().FirstOrDefault(candidate =>
            candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IList<>));
        return genericList?.GetGenericArguments()[0] ?? typeof(object);
    }

    private static object? DefaultValue(Type type) => type == typeof(string)
        ? string.Empty
        : type.IsValueType ? Activator.CreateInstance(type) : null;

    private static void MoveFixedSizeElement(IList values, int sourceIndex, int destinationIndex)
    {
        var value = values[sourceIndex];
        if (sourceIndex < destinationIndex)
            for (var index = sourceIndex; index < destinationIndex; index++) values[index] = values[index + 1];
        else
            for (var index = sourceIndex; index > destinationIndex; index--) values[index] = values[index - 1];
        values[destinationIndex] = value;
    }

    private static string GetDisplayName(string path, string fallback)
    {
        var bracket = path.LastIndexOf('[');
        if (bracket < 0 || !path.EndsWith(']')) return ObjectNames.NicifyVariableName(fallback);
        return int.TryParse(path.AsSpan(bracket + 1, path.Length - bracket - 2), out var index)
            ? $"Element {index}"
            : ObjectNames.NicifyVariableName(fallback);
    }

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
