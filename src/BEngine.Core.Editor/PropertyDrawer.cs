using System.Reflection;

namespace BEngine.Editor;

internal static class PropertyDrawerRegistry
{
    private static readonly object Gate = new();
    private static Registration[]? _registrations;

    static PropertyDrawerRegistry() =>
        AppDomain.CurrentDomain.AssemblyLoad += (_, _) =>
        {
            lock (Gate) _registrations = null;
        };

    public static bool TryCreate(SerializedProperty property, out PropertyDrawer drawer)
    {
        ArgumentNullException.ThrowIfNull(property);
        foreach (var propertyAttribute in property.GetAttributes().OfType<PropertyAttribute>()
                     .OrderBy(item => item.order))
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
        var registration = GetRegistrations()
            .Where(item => item.TargetType == targetType ||
                           item.UseForChildren && item.TargetType.IsAssignableFrom(targetType))
            .OrderByDescending(item => item.TargetType == targetType)
            .ThenBy(item => item.DrawerType.FullName, StringComparer.Ordinal)
            .FirstOrDefault();
        if (registration is null)
        {
            drawer = null!;
            return false;
        }

        drawer = (PropertyDrawer)Activator.CreateInstance(registration.DrawerType)!;
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
                .SelectMany(type => type.GetCustomAttributes<CustomPropertyDrawerAttribute>(inherit: false)
                    .Select(attribute => new Registration(type, attribute.type, attribute.useForChildren)))
                .ToArray();
        }
    }

    private sealed record Registration(Type DrawerType, Type TargetType, bool UseForChildren);
}
