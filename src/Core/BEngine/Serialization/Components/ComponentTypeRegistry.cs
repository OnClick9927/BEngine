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

        return null;
    }

    public void UnregisterAssembly(System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var typeName in _types.Where(pair => pair.Value.Assembly == assembly)
                     .Select(pair => pair.Key).ToArray())
            _types.Remove(typeName);
    }

    private static Type? ResolveLoadedComponent(string typeName)
    {
        var type = BEngine.RuntimeTypeCache.FindType(typeName);
        return type is not null && typeof(BEngine.Component).IsAssignableFrom(type) && !type.IsAbstract
            ? type : null;
    }

}
