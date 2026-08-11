namespace BEngine.Serialization;

public sealed class ComponentTypeRegistry
{
    private readonly Dictionary<string, Type> _types = new(StringComparer.Ordinal);

    public ComponentTypeRegistry Register<T>() where T : BEngine.Component => Register(typeof(T));

    public ComponentTypeRegistry Register(Type type)
    {
        if (!typeof(BEngine.Component).IsAssignableFrom(type) || type.IsAbstract)
        {
            throw new ArgumentException($"{type.FullName} is not a concrete Component type.", nameof(type));
        }

        if (type.FullName is { } fullName)
        {
            _types[fullName] = type;
        }

        return this;
    }

    public Type? Resolve(string typeName)
    {
        if (_types.TryGetValue(typeName, out var registered))
        {
            return registered;
        }

        var resolved = ResolveLoadedComponent(typeName);
        if (resolved is not null)
        {
            Register(resolved);
            return resolved;
        }

        foreach (var migratedTypeName in GetMigratedTypeNames(typeName))
        {
            resolved = ResolveLoadedComponent(migratedTypeName);
            if (resolved is null) continue;
            Register(resolved);
            _types[typeName] = resolved;
            return resolved;
        }

        return null;
    }

    private static Type? ResolveLoadedComponent(string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type is not null && typeof(BEngine.Component).IsAssignableFrom(type) && !type.IsAbstract)
            {
                return type;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetMigratedTypeNames(string typeName)
    {
        const string legacyNavigationPrefix = "BEngine.AI.";
        if (typeName.StartsWith(legacyNavigationPrefix, StringComparison.Ordinal))
        {
            yield return "BEngine.Navigation." + typeName[legacyNavigationPrefix.Length..];
            yield break;
        }

        const string legacyRootPrefix = "BEngine.";
        if (!typeName.StartsWith(legacyRootPrefix, StringComparison.Ordinal) ||
            typeName.IndexOf('.', legacyRootPrefix.Length) >= 0)
        {
            yield break;
        }

        var typeNameSuffix = typeName[legacyRootPrefix.Length..];
        yield return "BEngine.Physics3D." + typeNameSuffix;
        yield return "BEngine.Terrain." + typeNameSuffix;
        yield return "BEngine.Animation." + typeNameSuffix;
    }
}
