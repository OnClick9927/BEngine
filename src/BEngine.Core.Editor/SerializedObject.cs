using System.Collections;
using System.Reflection;

namespace BEngine.Editor;

public enum SerializedPropertyType
{
    Generic,
    Integer,
    Boolean,
    Float,
    String,
    Color,
    ObjectReference,
    LayerMask,
    Enum,
    Vector2,
    Vector3,
    Vector4,
    Quaternion,
    Rect,
    Bounds
}

public sealed class SerializedObject : IDisposable
{
    private bool _modified;
    private int _changeVersion;

    public BObject targetObject { get; }
    public BObject[] targetObjects { get; }
    public bool isEditingMultipleObjects => targetObjects.Length > 1;
    public bool hasModifiedProperties => _modified;

    public SerializedObject(BObject target) : this([target]) { }

    public static SerializedObject Create(BObject target) => new(target);

    public SerializedObject(BObject[] targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Length == 0 || targets.Any(target => target is null))
        {
            throw new ArgumentException("At least one non-null target is required.", nameof(targets));
        }
        targetObjects = [.. targets];
        targetObject = targetObjects[0];
    }

    public SerializedProperty? FindProperty(string propertyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyPath);
        return PropertyPath.TryResolve(targetObject, propertyPath, out _)
            ? new SerializedProperty(this, propertyPath)
            : null;
    }

    public SerializedProperty GetIterator() => SerializedProperty.CreateIterator(this, GetPropertyPaths());
    public SerializedProperty GetVisibleIterator() => GetIterator();

    public IEnumerable<SerializedProperty> GetVisibleProperties()
    {
        var iterator = GetIterator();
        while (iterator.NextVisible(enterChildren: false)) yield return iterator.Copy();
    }

    public void Update() => _modified = false;
    public void UpdateIfRequiredOrScript() => Update();

    public bool ApplyModifiedProperties()
    {
        if (!_modified) return false;
        foreach (var target in targetObjects) EditorUtility.SetDirty(target);
        _modified = false;
        return true;
    }

    public bool ApplyModifiedPropertiesWithoutUndo() => ApplyModifiedProperties();
    public void Dispose() { }

    internal object? GetValue(string path) => PropertyPath.Resolve(targetObject, path).GetValue();
    internal int changeVersion => _changeVersion;

    internal bool IsVisible(string path) => PropertyPath.Resolve(targetObject, path).MemberInfo?
        .GetCustomAttribute<HideInInspectorAttribute>() is null;

    internal void SetValue(string path, object? value)
    {
        if (!_modified) Undo.RecordObjects(targetObjects, $"Modify {PropertyPath.LeafName(path)}");
        foreach (var target in targetObjects) PropertyPath.Resolve(target, path).SetValue(value);
        _modified = true;
        _changeVersion++;
    }

    private string[] GetPropertyPaths() => ObjectState.GetSerializableMembers(targetObject)
        .Select(member => member.Name)
        .ToArray();
}

public sealed class SerializedProperty : IDisposable
{
    private readonly SerializedObject _serializedObject;
    private readonly string[]? _iteratorPaths;
    private int _iteratorIndex = -1;
    private string _propertyPath;
    private bool _isExpanded;

    public SerializedObject serializedObject => _serializedObject;
    public string propertyPath => _propertyPath;
    public string name => PropertyPath.LeafName(propertyPath);
    public string displayName => memberInfo?.GetCustomAttribute<InspectorNameAttribute>()?.displayName ??
                                 ObjectNames.NicifyVariableName(name);
    public string tooltip => memberInfo?.GetCustomAttribute<TooltipAttribute>()?.tooltip ?? string.Empty;
    public string type => valueType.Name;
    public Type valueType => PropertyPath.Resolve(_serializedObject.targetObject, propertyPath).ValueType;
    public MemberInfo? memberInfo => PropertyPath.Resolve(_serializedObject.targetObject, propertyPath).MemberInfo;
    public int depth => propertyPath.Count(character => character == '.');
    public bool editable => PropertyPath.Resolve(_serializedObject.targetObject, propertyPath).CanWrite;
    public bool hasMultipleDifferentValues => _serializedObject.targetObjects
        .Select(target => PropertyPath.Resolve(target, propertyPath).GetValue())
        .Distinct().Skip(1).Any();
    public bool isArray => boxedValue is IList or Array;
    public bool hasVisibleChildren => isArray && arraySize > 0;
    public bool isExpanded { get => _isExpanded; set => _isExpanded = value; }
    public SerializedPropertyType propertyType => GetPropertyType(
        PropertyPath.Resolve(_serializedObject.targetObject, propertyPath).ValueType);

    internal SerializedProperty(SerializedObject serializedObject, string propertyPath)
    {
        _serializedObject = serializedObject;
        _propertyPath = propertyPath;
    }

    private SerializedProperty(SerializedObject serializedObject, string[] iteratorPaths)
    {
        _serializedObject = serializedObject;
        _iteratorPaths = iteratorPaths;
        _propertyPath = string.Empty;
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
    public Vector3 vector3Value { get => boxedValue is Vector3 value ? value : Vector3.zero; set => boxedValue = value; }
    public Vector4 vector4Value { get => boxedValue is Vector4 value ? value : Vector4.zero; set => boxedValue = value; }
    public Quaternion quaternionValue { get => boxedValue is Quaternion value ? value : Quaternion.identity; set => boxedValue = value; }
    public Color colorValue { get => boxedValue is Color value ? value : Color.white; set => boxedValue = value; }
    public Rect rectValue { get => boxedValue is Rect value ? value : default; set => boxedValue = value; }
    public Bounds boundsValue { get => boxedValue is Bounds value ? value : default; set => boxedValue = value; }
    public BObject? objectReferenceValue { get => boxedValue as BObject; set => boxedValue = value; }
    public int objectReferenceInstanceIDValue => objectReferenceValue?.GetInstanceID() ?? 0;

    public int enumValueIndex
    {
        get => boxedValue is Enum value ? Convert.ToInt32(value) : intValue;
        set
        {
            var valueType = PropertyPath.Resolve(_serializedObject.targetObject, propertyPath).ValueType;
            boxedValue = valueType.IsEnum ? Enum.ToObject(valueType, value) : ConvertNumeric(value);
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

    public SerializedProperty Copy() => new(_serializedObject, propertyPath) { isExpanded = _isExpanded };
    public void Dispose() { }

    private object ConvertNumeric(object value)
    {
        var valueType = PropertyPath.Resolve(_serializedObject.targetObject, propertyPath).ValueType;
        return Convert.ChangeType(value, valueType, System.Globalization.CultureInfo.InvariantCulture);
    }

    internal T? GetAttribute<T>() where T : Attribute => memberInfo?.GetCustomAttribute<T>();

    internal Attribute[] GetAttributes() => memberInfo?.GetCustomAttributes(inherit: true)
        .OfType<Attribute>().ToArray() ?? [];

    private bool MoveNext(bool visibleOnly)
    {
        if (_iteratorPaths is null) return false;
        while (++_iteratorIndex < _iteratorPaths.Length)
        {
            var path = _iteratorPaths[_iteratorIndex];
            if (visibleOnly && !_serializedObject.IsVisible(path)) continue;
            _propertyPath = path;
            return true;
        }
        _propertyPath = string.Empty;
        return false;
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
        if (type == typeof(Vector3)) return SerializedPropertyType.Vector3;
        if (type == typeof(Vector4)) return SerializedPropertyType.Vector4;
        if (type == typeof(Quaternion)) return SerializedPropertyType.Quaternion;
        if (type == typeof(Color)) return SerializedPropertyType.Color;
        if (type == typeof(Rect)) return SerializedPropertyType.Rect;
        if (type == typeof(Bounds)) return SerializedPropertyType.Bounds;
        if (typeof(BObject).IsAssignableFrom(type)) return SerializedPropertyType.ObjectReference;
        return SerializedPropertyType.Generic;
    }
}

internal static class PropertyPath
{
    public static bool TryResolve(object target, string path, out PropertyAccessor accessor)
    {
        try
        {
            accessor = Resolve(target, path);
            return true;
        }
        catch (ArgumentException)
        {
            accessor = default;
            return false;
        }
    }

    public static PropertyAccessor Resolve(object target, string path)
    {
        object current = target;
        var segments = path.Replace(".Array.data[", "[").Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var bracket = segment.IndexOf('[');
            var memberName = bracket >= 0 ? segment[..bracket] : segment;
            var accessor = FindMember(current, memberName);
            if (index == segments.Length - 1 && bracket < 0) return accessor;
            current = accessor.GetValue() ?? throw new ArgumentException($"Property '{memberName}' is null.", nameof(path));
            if (bracket < 0) continue;
            var end = segment.IndexOf(']', bracket + 1);
            if (end < 0 || !int.TryParse(segment[(bracket + 1)..end], out var itemIndex))
                throw new ArgumentException($"Invalid array property path '{path}'.", nameof(path));
            if (current is not IList list || itemIndex < 0 || itemIndex >= list.Count)
                throw new ArgumentException($"Array index is outside '{path}'.", nameof(path));
            var listAccessor = new PropertyAccessor(list, itemIndex);
            if (index == segments.Length - 1) return listAccessor;
            current = listAccessor.GetValue() ?? throw new ArgumentException($"Array item in '{path}' is null.", nameof(path));
        }
        throw new ArgumentException($"Property '{path}' was not found.", nameof(path));
    }

    public static string LeafName(string path)
    {
        var leaf = path[(path.LastIndexOf('.') + 1)..];
        var bracket = leaf.IndexOf('[');
        return bracket < 0 ? leaf : leaf[..bracket];
    }

    private static PropertyAccessor FindMember(object owner, string name)
    {
        for (var type = owner.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public |
                                            BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is not null) return new PropertyAccessor(owner, field);
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public |
                                                  BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property is not null && property.CanRead && property.CanWrite)
                return new PropertyAccessor(owner, property);
        }
        throw new ArgumentException($"Property '{name}' was not found on {owner.GetType().Name}.");
    }
}

internal readonly struct PropertyAccessor
{
    private readonly object _owner;
    private readonly MemberInfo? _member;
    private readonly IList? _list;
    private readonly int _index;

    public MemberInfo? MemberInfo => _member;
    public bool CanWrite => _member switch
    {
        FieldInfo field => !field.IsInitOnly,
        PropertyInfo property => property.SetMethod is not null,
        _ => _list is not null && !_list.IsReadOnly
    };

    public Type ValueType => _member switch
    {
        FieldInfo field => field.FieldType,
        PropertyInfo property => property.PropertyType,
        _ when _list is not null => _list.GetType().IsArray
            ? _list.GetType().GetElementType()!
            : _list.GetType().IsGenericType ? _list.GetType().GetGenericArguments()[0] : typeof(object),
        _ => typeof(object)
    };

    public PropertyAccessor(object owner, MemberInfo member)
    {
        _owner = owner;
        _member = member;
        _list = null;
        _index = -1;
    }

    public PropertyAccessor(IList list, int index)
    {
        _owner = list;
        _member = null;
        _list = list;
        _index = index;
    }

    public object? GetValue() => _member switch
    {
        FieldInfo field => field.GetValue(_owner),
        PropertyInfo property => property.GetValue(_owner),
        _ when _list is not null => _list[_index],
        _ => null
    };

    public void SetValue(object? value)
    {
        switch (_member)
        {
            case FieldInfo field:
                field.SetValue(_owner, value);
                break;
            case PropertyInfo property:
                property.SetValue(_owner, value);
                break;
            default:
                _list![_index] = value;
                break;
        }
    }
}

public static class ObjectNames
{
    public static string NicifyVariableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var result = new System.Text.StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (character == '_')
            {
                if (result.Length > 0 && result[^1] != ' ') result.Append(' ');
                continue;
            }
            if (index > 0 && char.IsUpper(character) && result[^1] != ' ') result.Append(' ');
            result.Append(character);
        }
        if (result.Length > 0) result[0] = char.ToUpperInvariant(result[0]);
        return result.ToString();
    }
}
