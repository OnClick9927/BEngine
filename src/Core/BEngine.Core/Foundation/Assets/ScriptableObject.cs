namespace BEngine;

public abstract class ScriptableObject : BAsset
{
    protected ScriptableObject() => name = GetType().Name;

    public static T CreateInstance<T>() where T : ScriptableObject, new()
    {
        return new T();
    }

    public static ScriptableObject CreateInstance(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!typeof(ScriptableObject).IsAssignableFrom(type) || type.IsAbstract)
        {
            throw new ArgumentException($"{type.FullName} is not a concrete ScriptableObject type.", nameof(type));
        }

        return RuntimeTypeCache.TryCreateInstance(type, out var instance) && instance is ScriptableObject result
            ? result : throw new InvalidOperationException($"Unable to create {type.FullName}.");
    }
}
