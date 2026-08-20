namespace BEngine;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequireComponentAttribute : Attribute
{
    public IReadOnlyList<Type> requiredComponents { get; }

    public RequireComponentAttribute(Type requiredComponent)
        : this(requiredComponent, null, null) { }

    public RequireComponentAttribute(Type requiredComponent, Type? requiredComponent2)
        : this(requiredComponent, requiredComponent2, null) { }

    public RequireComponentAttribute(Type requiredComponent, Type? requiredComponent2, Type? requiredComponent3)
    {
        requiredComponents = new[] { requiredComponent, requiredComponent2, requiredComponent3 }
            .Where(type => type is not null).Cast<Type>().ToArray();
        if (requiredComponents.Any(type => !typeof(Component).IsAssignableFrom(type)))
        {
            throw new ArgumentException("Required types must derive from Component.");
        }
    }
}
