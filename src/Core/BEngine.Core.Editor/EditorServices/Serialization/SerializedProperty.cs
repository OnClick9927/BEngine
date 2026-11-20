using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

public sealed class SerializedProperty : IDisposable
{
    private static readonly ConditionalWeakTable<Type, EnumData> EnumCache = new();
    private readonly SerializedObject _serializedObject;
    private readonly string[]? _iteratorPaths;
    private bool _iteratorEnded;
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
            return _serializedObject.HasMultipleDifferentValues(propertyPath);
        }
    }
    public bool isArray => IsSupportedCollectionType(valueType);
    public bool hasVisibleChildren => _serializedObject.HasVisibleChildren(this);
    public bool isExpanded { get; set; }
    public SerializedPropertyType propertyType => IsLayerMaskProperty()
        ? SerializedPropertyType.LayerMask
        : GetPropertyType(valueType);

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

    public ulong ulongValue
    {
        get => boxedValue is LayerMask mask
            ? mask.value
            : Convert.ToUInt64(boxedValue, System.Globalization.CultureInfo.InvariantCulture);
        set => boxedValue = valueType == typeof(LayerMask) ? new LayerMask(value) : ConvertNumeric(value);
    }

    public bool boolValue { get => boxedValue is true; set => boxedValue = value; }
    public string stringValue { get => boxedValue?.ToString() ?? string.Empty; set => boxedValue = value; }

    public float floatValue
    {
        get => boxedValue is Fix64 fixedValue ? (float)fixedValue : Convert.ToSingle(boxedValue);
        set => boxedValue = _serializedObject.GetAccessor(propertyPath).ValueType == typeof(Fix64)
            ? (Fix64)value
            : ConvertNumeric(value);
    }

    public double doubleValue
    {
        get => boxedValue is Fix64 fixedValue ? (double)fixedValue : Convert.ToDouble(boxedValue);
        set => boxedValue = _serializedObject.GetAccessor(propertyPath).ValueType == typeof(Fix64)
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
            var data = EnumCache.GetValue(value.GetType(), static type => new EnumData(type));
            return data.Indices.TryGetValue(value, out var index) ? index : 0;
        }
        set
        {
            var valueType = _serializedObject.GetAccessor(propertyPath).ValueType;
            if (!valueType.IsEnum) { boxedValue = ConvertNumeric(value); return; }
            var values = EnumCache.GetValue(valueType, static type => new EnumData(type)).Values;
            boxedValue = values.GetValue(Math.Clamp(value, 0, Math.Max(0, values.Length - 1)));
        }
    }

    public string[] enumDisplayNames => _serializedObject.GetAccessor(propertyPath)
        .ValueType is { IsEnum: true } enumType ? Enum.GetNames(enumType) : [];

    private sealed class EnumData
    {
        internal readonly Dictionary<Enum, int> Indices = [];
        internal readonly Array Values;

        internal EnumData(Type type)
        {
            var values = Values = Enum.GetValues(type);
            for (var index = 0; index < values.Length; index++)
                Indices.TryAdd((Enum)values.GetValue(index)!, index);
        }
    }

    public int arraySize
    {
        get
        {
            return _serializedObject.GetMinimumCollectionSize(propertyPath);
        }
        set
        {
            var count = Math.Max(0, value);
            var values = GetCollectionValues();
            var projectionSource6620 = values;
            ArgumentNullException.ThrowIfNull(projectionSource6620, "source");
            var projectionResult6620 = projectionSource6620.Length == 0 ? global::System.Array.Empty<object?>() : new object?[projectionSource6620.Length];
            for (var projectionIndex6620 = 0; projectionIndex6620 < projectionSource6620.Length; projectionIndex6620++)
            {
                var projectionItem6620 = projectionSource6620[projectionIndex6620];
                projectionResult6620[projectionIndex6620] = ResizeCollection(projectionItem6620, valueType, count);
            }
            
            var resized = projectionResult6620;
            _serializedObject.SetValues(propertyPath, resized);
        }
    }

    public SerializedProperty? FindPropertyRelative(string relativePropertyPath) =>
        _serializedObject.FindProperty($"{propertyPath}.{relativePropertyPath}");

    public bool Next(bool enterChildren) => MoveNext(enterChildren, visibleOnly: false);
    public bool NextVisible(bool enterChildren) => MoveNext(enterChildren, visibleOnly: true);

    public SerializedProperty GetArrayElementAtIndex(int index)
    {
        if (index < 0 || index >= arraySize) throw new ArgumentOutOfRangeException(nameof(index));
        return _serializedObject.GetOrCreateProperty($"{propertyPath}[{index}]");
    }

    public void InsertArrayElementAtIndex(int index)
    {
        var values = GetCollectionValues();
        var projectionSource7514 = values;
        ArgumentNullException.ThrowIfNull(projectionSource7514, "source");
        var projectionResult7514 = projectionSource7514.Length == 0 ? global::System.Array.Empty<object?>() : new object?[projectionSource7514.Length];
        for (var projectionIndex7514 = 0; projectionIndex7514 < projectionSource7514.Length; projectionIndex7514++)
        {
            var projectionItem7514 = projectionSource7514[projectionIndex7514];
            projectionResult7514[projectionIndex7514] = InsertCollectionElement(projectionItem7514, valueType, index);
        }
        
        var inserted = projectionResult7514;
        _serializedObject.SetValues(propertyPath, inserted);
    }

    public void DeleteArrayElementAtIndex(int index)
    {
        if (index < 0 || index >= arraySize) throw new ArgumentOutOfRangeException(nameof(index));
        var values = GetCollectionValues();
        var projectionSource7890 = values;
        ArgumentNullException.ThrowIfNull(projectionSource7890, "source");
        var projectionResult7890 = projectionSource7890.Length == 0 ? global::System.Array.Empty<object?>() : new object?[projectionSource7890.Length];
        for (var projectionIndex7890 = 0; projectionIndex7890 < projectionSource7890.Length; projectionIndex7890++)
        {
            var projectionItem7890 = projectionSource7890[projectionIndex7890];
            projectionResult7890[projectionIndex7890] = DeleteCollectionElement(projectionItem7890!, valueType, index);
        }
        
        var deleted = projectionResult7890;
        _serializedObject.SetValues(propertyPath, deleted);
    }

    public bool MoveArrayElement(int sourceIndex, int destinationIndex)
    {
        var count = arraySize;
        if (sourceIndex < 0 || sourceIndex >= count || destinationIndex < 0 || destinationIndex >= count)
            return false;
        if (sourceIndex == destinationIndex) return true;
        var values = GetCollectionValues();
        bool __queryResult8397;
        {
            var __querySource8397 = values;
            ArgumentNullException.ThrowIfNull(__querySource8397, "source");
            __queryResult8397 = false;
            foreach (var __queryItem8397 in __querySource8397)
            {
                if ((__queryItem8397 is not IList list || list.IsReadOnly || list.IsFixedSize && __queryItem8397 is not Array))
                {
                    __queryResult8397 = true;
                    break;
                }
            }
        }
        
        if (__queryResult8397)
            return false;
        var projectionSource9014 = values;
        ArgumentNullException.ThrowIfNull(projectionSource9014, "source");
        var projectionResult9014 = projectionSource9014.Length == 0 ? global::System.Array.Empty<object?>() : new object?[projectionSource9014.Length];
        for (var projectionIndex9014 = 0; projectionIndex9014 < projectionSource9014.Length; projectionIndex9014++)
        {
            var projectionItem9014 = projectionSource9014[projectionIndex9014];
            projectionResult9014[projectionIndex9014] = MoveCollectionElement((IList)projectionItem9014!, valueType, sourceIndex, destinationIndex);
        }
        
        var reordered = projectionResult9014;
        _serializedObject.SetValues(propertyPath, reordered);
        return true;
    }

    public void ClearArray()
    {
        var values = GetCollectionValues();
        var projectionSource9345 = values;
        ArgumentNullException.ThrowIfNull(projectionSource9345, "source");
        var projectionResult9345 = projectionSource9345.Length == 0 ? global::System.Array.Empty<object?>() : new object?[projectionSource9345.Length];
        for (var projectionIndex9345 = 0; projectionIndex9345 < projectionSource9345.Length; projectionIndex9345++)
        {
            var projectionItem9345 = projectionSource9345[projectionIndex9345];
            projectionResult9345[projectionIndex9345] = ResizeCollection(projectionItem9345, valueType, 0);
        }
        
        var cleared = projectionResult9345;
        _serializedObject.SetValues(propertyPath, cleared);
    }

    public SerializedProperty Copy() => (SerializedProperty)MemberwiseClone();
    public void Dispose() { }

    internal IReadOnlyList<SerializedProperty> GetVisibleChildren() =>
        _serializedObject.GetVisibleChildren(this);

    internal bool TryGetBoxedValue(out object? value) =>
        _serializedObject.TryGetValue(propertyPath, out value);

    private object ConvertNumeric(object value)
    {
        var valueType = _serializedObject.GetAccessor(propertyPath).ValueType;
        return Convert.ChangeType(value, valueType, System.Globalization.CultureInfo.InvariantCulture);
    }

    internal T? GetAttribute<T>() where T : Attribute =>
        SerializedMemberMetadata.For(memberInfo).Attributes.OfType<T>().FirstOrDefault();

    internal Attribute[] GetAttributes() => SerializedMemberMetadata.For(memberInfo).Attributes;

    private bool MoveNext(bool enterChildren, bool visibleOnly)
    {
        if (_iteratorEnded) return false;
        string? next = null;
        if (_propertyPath.Length == 0)
        {
            next = Matches13310();
            [global::BEngine.SkipLoopGuard]
            string? Matches13310()
            {
                var matchSource13310 = (_iteratorPaths ?? _serializedObject.GetPropertyPaths());
                ArgumentNullException.ThrowIfNull(matchSource13310, "source");
                global::System.ReadOnlySpan<string> matchValues13310 = matchSource13310;
                foreach (var path in matchValues13310)
                {
                    if (!visibleOnly || _serializedObject.IsVisible(path))
                        return path;
                }
            
                return default!;
            }
        }
        else
        {
            if (enterChildren)
                next = _serializedObject.GetTraversalChildren(this, visibleOnly).FirstOrDefault()?.propertyPath;
            var current = PropertyPath.Normalize(_propertyPath);
            while (next is null && current.Length > 0)
            {
                var parent = PropertyPath.ParentPath(current);
                var siblings = parent.Length == 0
                    ? _serializedObject.GetPropertyPaths().AsEnumerable()
                    : _serializedObject.GetTraversalChildren(_serializedObject.GetOrCreateProperty(parent), visibleOnly: false)
                        .Select(property => property.propertyPath);
                var found = false;
                foreach (var sibling in siblings)
                {
                    if (found && (!visibleOnly || _serializedObject.IsVisible(sibling))) { next = sibling; break; }
                    found |= PropertyPath.Normalize(sibling) == current;
                }
                current = parent;
            }
        }
        _serializedObject.DetachPropertyCursor(this);
        if (next is not null)
        {
            _propertyPath = next;
            RefreshMetadata();
            return true;
        }
        _iteratorEnded = true;
        _propertyPath = string.Empty;
        _name = string.Empty;
        _depth = 0;
        return false;
    }

    private void RefreshMetadata()
    {
        _name = PropertyPath.LeafName(_propertyPath);
        var accessor = _serializedObject.GetAccessor(_propertyPath);
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
        bool __queryResult12563;
        {
            var __querySource12563 = values;
            ArgumentNullException.ThrowIfNull(__querySource12563, "source");
            __queryResult12563 = false;
            foreach (var __queryItem12563 in __querySource12563)
            {
                if ((__queryItem12563 is not null and not IList))
                {
                    __queryResult12563 = true;
                    break;
                }
            }
        }
        
        if (__queryResult12563)
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
                ? EditorValueCloner.CloneSerializedBufferValue(source.GetValue(insertion - 1))
                : DefaultValue(elementType), insertion);
            return resized;
        }

        var sourceList = value as IList;
        var resizedList = sourceList is null
            ? CreateResizableList(declaredType, elementType)
            : CloneResizableList(sourceList);
        var insertionIndex = Math.Clamp(requestedIndex, 0, resizedList.Count);
        var insertedValue = insertionIndex > 0
            ? EditorValueCloner.CloneSerializedBufferValue(resizedList[insertionIndex - 1])
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
        bool __queryResult18427;
        {
            var __querySource18427 = type.GetInterfaces();
            ArgumentNullException.ThrowIfNull(__querySource18427, "source");
            __queryResult18427 = false;
            foreach (var __queryItem18427 in __querySource18427)
            {
                if ((__queryItem18427.IsGenericType && __queryItem18427.GetGenericTypeDefinition() == typeof(IList<>)))
                {
                    __queryResult18427 = true;
                    break;
                }
            }
        }
        
        return __queryResult18427;
    }

    private static Type ElementType(Type collectionType)
    {
        if (collectionType.IsArray) return collectionType.GetElementType() ?? typeof(object);
        if (collectionType.IsGenericType && collectionType.GetGenericArguments().Length == 1)
            return collectionType.GetGenericArguments()[0];
        var genericList = Matches23000();
        [global::BEngine.SkipLoopGuard]
        global::System.Type? Matches23000()
        {
            var matchSource23000 = collectionType.GetInterfaces();
            ArgumentNullException.ThrowIfNull(matchSource23000, "source");
            global::System.ReadOnlySpan<global::System.Type> matchValues23000 = matchSource23000;
            foreach (var candidate in matchValues23000)
            {
                if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IList<>))
                    return candidate;
            }
        
            return default!;
        }
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

    private bool IsLayerMaskProperty() =>
        valueType == typeof(LayerMask) ||
        IsIntegralType(valueType) &&
        (name.Equals("cullingMask", StringComparison.OrdinalIgnoreCase) ||
         name.EndsWith("LayerMask", StringComparison.OrdinalIgnoreCase));

    private static bool IsIntegralType(Type type) =>
        type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long) ||
        type == typeof(uint) || type == typeof(ulong);
}
