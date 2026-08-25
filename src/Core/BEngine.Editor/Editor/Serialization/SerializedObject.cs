using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

public sealed class SerializedObject : IDisposable
{
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
        return PropertyPath.TryResolve(targetObject, propertyPath, out _)
            ? _properties[propertyPath] = new SerializedProperty(this, propertyPath)
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
    internal int changeVersion => _changeVersion;

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
}
