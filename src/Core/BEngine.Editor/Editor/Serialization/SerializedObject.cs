using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

public sealed class SerializedObject : IDisposable
{
    private static readonly ConditionalWeakTable<Type, MemberInfo[]> ChildMemberCache = new();
    private bool _modified;
    private int _changeVersion;
    private readonly string[] _propertyPaths;
    private readonly SerializedProperty[] _visibleProperties;
    private readonly Dictionary<string, SerializedProperty> _properties = new(StringComparer.Ordinal);

    public BObject targetObject { get; }
    public BObject[] targetObjects { get; }
    public bool isEditingMultipleObjects => targetObjects.Length > 1;
    public bool hasModifiedProperties => _modified;
    public event Action<SerializedObject>? changed;

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
        _propertyPaths = ObjectState.GetSerializableMembers(targetObject)
            .Select(member => member.Name)
            .ToArray();
        _visibleProperties = _propertyPaths.Where(IsVisible)
            .Select(path => new SerializedProperty(this, path)).ToArray();
        foreach (var property in _visibleProperties) _properties[property.propertyPath] = property;
    }

    public SerializedProperty? FindProperty(string propertyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyPath);
        if (_properties.TryGetValue(propertyPath, out var cached)) return cached;
        return TryResolve(targetObject, propertyPath, out _)
            ? GetOrCreateProperty(propertyPath)
            : null;
    }

    public SerializedProperty GetIterator() => SerializedProperty.CreateIterator(this, GetPropertyPaths());
    public SerializedProperty GetVisibleIterator() => GetIterator();

    public SerializedProperty[] GetVisibleProperties() => _visibleProperties;

    public void Update() => _modified = false;
    public void UpdateIfRequiredOrScript() => Update();

    public bool ApplyModifiedProperties()
    {
        if (!_modified) return false;
        foreach (var target in targetObjects)
        {
            EditorUtility.SetDirty(target);
            if (target is not MonoBehaviour behaviour) continue;
            EditorFeatureGuard.Invoke(behaviour, nameof(MonoBehaviour.OnValidate), behaviour.OnValidate);
        }
        _modified = false;
        return true;
    }

    public bool ApplyModifiedPropertiesWithoutUndo() => ApplyModifiedProperties();
    public void Dispose() { }

    internal object? GetValue(string path) => PropertyPath.Resolve(targetObject, path).GetValue();
    internal bool TryGetValue(string path, out object? value) =>
        TryGetValue(targetObject, path, out value);
    internal int changeVersion => _changeVersion;

    internal IReadOnlyList<SerializedProperty> GetVisibleChildren(SerializedProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (!TryGetValues(property.propertyPath, out var values) || values[0] is not { } value) return [];

        if (value is IList list)
        {
            var count = list.Count;
            for (var index = 1; index < values.Length; index++)
            {
                if (values[index] is not IList candidate) return [];
                count = Math.Min(count, candidate.Count);
            }
            var children = new List<SerializedProperty>(count);
            for (var index = 0; index < count; index++)
            {
                var path = $"{property.propertyPath}[{index}]";
                if (CanResolveForAllTargets(path)) children.Add(GetOrCreateProperty(path));
            }
            return children;
        }

        var runtimeType = value.GetType();
        if (runtimeType.IsValueType || value is string or BObject || typeof(Delegate).IsAssignableFrom(runtimeType))
            return [];
        if (values.Skip(1).Any(candidate => candidate is null || !runtimeType.IsInstanceOfType(candidate))) return [];

        return GetSerializableChildMembers(runtimeType)
            .Select(member => $"{property.propertyPath}.{member.Name}")
            .Where(CanResolveForAllTargets)
            .Select(GetOrCreateProperty)
            .ToArray();
    }

    internal bool IsVisible(string path) =>
        !SerializedMemberMetadata.For(PropertyPath.Resolve(targetObject, path).MemberInfo).IsHidden;

    internal void SetValue(string path, object? value)
    {
        var accessors = targetObjects.Select(target => PropertyPath.Resolve(target, path)).ToArray();
        if (accessors.All(accessor => Equals(accessor.GetValue(), value))) return;
        foreach (var accessor in accessors)
        {
            if (!accessor.CanWrite)
                throw new InvalidOperationException($"Property '{path}' is read-only.");
            var valueType = accessor.ValueType;
            if (value is null)
            {
                if (valueType.IsValueType && Nullable.GetUnderlyingType(valueType) is null)
                    throw new ArgumentException($"Null cannot be assigned to {valueType.Name}.", nameof(value));
            }
            else if (!valueType.IsInstanceOfType(value))
                throw new ArgumentException(
                    $"Object of type {value.GetType().Name} cannot be assigned to {valueType.Name}.",
                    nameof(value));
        }
        if (!_modified) Undo.RecordObjects(targetObjects, $"Modify {PropertyPath.LeafName(path)}");
        foreach (var accessor in accessors)
            if (!Equals(accessor.GetValue(), value)) accessor.SetValue(value);
        _modified = true;
        _changeVersion++;
        EditorCallbackDispatcher.Invoke(changed, this, nameof(changed));
    }

    private string[] GetPropertyPaths() => _propertyPaths;

    private SerializedProperty GetOrCreateProperty(string path)
    {
        if (_properties.TryGetValue(path, out var cached)) return cached;
        return _properties[path] = new SerializedProperty(this, path);
    }

    private bool CanResolveForAllTargets(string path)
    {
        foreach (var target in targetObjects)
            if (!TryResolve(target, path, out _)) return false;
        return true;
    }

    internal bool TryGetValues(string path, out object?[] values)
    {
        values = new object?[targetObjects.Length];
        for (var index = 0; index < targetObjects.Length; index++)
        {
            if (!TryGetValue(targetObjects[index], path, out values[index])) return false;
        }
        return true;
    }

    private bool TryGetValue(BObject target, string path, out object? value) =>
        EditorFeatureGuard.TryInvoke(ReadFeatureName(target, path),
            () => PropertyPath.Resolve(target, path).GetValue(), fallback: null, out value);

    private bool TryResolve(BObject target, string path, out PropertyAccessor accessor)
    {
        var resolved = default(PropertyAccessor);
        var wasResolved = false;
        var succeeded = EditorFeatureGuard.Invoke(ReadFeatureName(target, path),
            () => wasResolved = PropertyPath.TryResolve(target, path, out resolved));
        accessor = resolved;
        return succeeded && wasResolved;
    }

    private static string ReadFeatureName(BObject target, string path) =>
        $"SerializedProperty {target.GetType().FullName ?? target.GetType().Name}.{path}.get";

    private static MemberInfo[] GetSerializableChildMembers(Type type)
    {
        return ChildMemberCache.GetValue(type, static childType =>
            RuntimeTypeCache.GetInstanceMembers(childType)
                .Where(IsSerializableChildMember)
                .Where(member => !SerializedMemberMetadata.For(member).IsHidden)
                .GroupBy(member => member.Name, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(member => member.Name, StringComparer.Ordinal)
                .ToArray());
    }

    private static bool IsSerializableChildMember(MemberInfo member) => member switch
    {
        FieldInfo field => !field.IsStatic && !field.IsInitOnly &&
                           !typeof(Delegate).IsAssignableFrom(field.FieldType) &&
                           !field.IsDefined(typeof(CompilerGeneratedAttribute), inherit: true) &&
                           !field.IsDefined(typeof(NonSerializedAttribute), inherit: true) &&
                           (field.IsPublic || field.IsDefined(typeof(SerializeFieldAttribute), inherit: true) ||
                            field.IsDefined(typeof(SerializeReferenceAttribute), inherit: true)),
        PropertyInfo property => property.GetIndexParameters().Length == 0 &&
                                 property.GetMethod is { IsPublic: true } &&
                                 property.SetMethod is { IsPublic: true } &&
                                 !typeof(Delegate).IsAssignableFrom(property.PropertyType),
        _ => false
    };
}
