namespace BEngine;

public abstract class ScriptableObject : BObject
{
    protected ScriptableObject() => name = GetType().Name;

    public static T CreateInstance<T>() where T : ScriptableObject, new() => new();

    public static ScriptableObject CreateInstance(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!typeof(ScriptableObject).IsAssignableFrom(type) || type.IsAbstract)
        {
            throw new ArgumentException($"{type.FullName} is not a concrete ScriptableObject type.", nameof(type));
        }

        return (ScriptableObject?)Activator.CreateInstance(type, nonPublic: true) ??
            throw new InvalidOperationException($"Unable to create {type.FullName}.");
    }
}
