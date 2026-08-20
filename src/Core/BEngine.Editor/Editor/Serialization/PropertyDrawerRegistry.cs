using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal static class PropertyDrawerRegistry
{
    private static readonly object Gate = new();
    private static Registration[]? _registrations;
    private static readonly Dictionary<Type, Registration?> Resolved = [];
    private static readonly ConditionalWeakTable<SerializedObject, Dictionary<DrawerKey, PropertyDrawer>> Drawers = new();

    static PropertyDrawerRegistry() =>
        AppDomain.CurrentDomain.AssemblyLoad += (_, _) =>
        {
            lock (Gate) _registrations = null;
        };

    internal static void Invalidate()
    {
        lock (Gate) { _registrations = null; Resolved.Clear(); Drawers.Clear(); }
    }

    internal static void Warmup() => _ = GetRegistrations();

    public static bool TryCreate(SerializedProperty property, out PropertyDrawer drawer)
    {
        ArgumentNullException.ThrowIfNull(property);
        foreach (var propertyAttribute in property.GetPropertyAttributes())
        {
            if (TryCreate(property, propertyAttribute.GetType(), propertyAttribute, out drawer)) return true;
        }

        return TryCreate(property, property.valueType, null, out drawer);
    }

    private static bool TryCreate(
        SerializedProperty property,
        Type targetType,
        PropertyAttribute? propertyAttribute,
        out PropertyDrawer drawer)
    {
        var registration = Resolve(targetType);
        if (registration is null)
        {
            drawer = null!;
            return false;
        }

        var drawers = Drawers.GetOrCreateValue(property.serializedObject);
        var key = new DrawerKey(property.propertyPath, registration.Value.DrawerType,
            propertyAttribute?.GetType());
        lock (drawers)
        {
            if (!drawers.TryGetValue(key, out drawer!))
            {
                if (!EditorFeatureGuard.TryInvoke<PropertyDrawer?>(
                        $"PropertyDrawer {registration.Value.DrawerType.FullName}.CreateInstance",
                        () => (PropertyDrawer)registration.Value.Factory(), null, out var created) ||
                    created is null)
                {
                    drawer = null!;
                    return false;
                }
                drawer = created;
                drawers.Add(key, drawer);
            }
        }
        drawer.attribute = propertyAttribute;
        drawer.memberInfo = property.memberInfo;
        drawer.fieldInfo = property.memberInfo as FieldInfo;
        return true;
    }

    private static Registration[] GetRegistrations()
    {
        lock (Gate)
        {
            return _registrations ??= TypeCache.GetTypesDerivedFrom<PropertyDrawer>()
                .Where(type => !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) is not null)
                .Select(type => (Type: type, Factory: RuntimeTypeCache.GetFactory(type)))
                .Where(item => item.Factory is not null)
                .SelectMany(item => RegistrationsFor(item.Type, item.Factory!))
                .ToArray();
        }
    }

    private static Registration[] RegistrationsFor(Type type, Func<object> factory)
    {
        var feature = $"CustomPropertyDrawer attributes {type.FullName}";
        return EditorFeatureGuard.TryInvoke(feature,
            () => type.GetCustomAttributes<CustomPropertyDrawerAttribute>(inherit: false)
                .Select(attribute => new Registration(type, attribute.type,
                    attribute.useForChildren, factory)).ToArray(), [], out var registrations)
            ? registrations : [];
    }

    private static Registration? Resolve(Type targetType)
    {
        lock (Gate)
        {
            if (Resolved.TryGetValue(targetType, out var cached)) return cached;
            Registration? result = GetRegistrations()
                .Where(item => item.TargetType == targetType ||
                               item.UseForChildren && item.TargetType.IsAssignableFrom(targetType))
                .OrderByDescending(item => item.TargetType == targetType)
                .ThenBy(item => item.DrawerType.FullName, StringComparer.Ordinal)
                .Cast<Registration?>().FirstOrDefault();
            Resolved[targetType] = result;
            return result;
        }
    }

    private readonly record struct Registration(
        Type DrawerType,
        Type TargetType,
        bool UseForChildren,
        Func<object> Factory);
    private readonly record struct DrawerKey(string PropertyPath, Type DrawerType, Type? AttributeType);
}
