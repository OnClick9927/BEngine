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
        bool __queryResult782;
        {
            var __querySource782 = requiredComponents;
            ArgumentNullException.ThrowIfNull(__querySource782, "source");
            __queryResult782 = false;
            var __querySource782Type = __querySource782.GetType();
            if (__querySource782Type == typeof(System.Type[]) || __querySource782Type == typeof(System.Collections.Generic.List<System.Type>))
            {
                System.ReadOnlySpan<System.Type> __querySource782Span = __querySource782Type == typeof(System.Type[]) ? (System.Type[])(object)__querySource782 : System.Runtime.InteropServices.CollectionsMarshal.AsSpan((System.Collections.Generic.List<System.Type>)(object)__querySource782);
                foreach (var __queryItem782 in __querySource782Span)
                {
                    if ((!typeof(Component).IsAssignableFrom(__queryItem782)))
                    {
                        __queryResult782 = true;
                        break;
                    }
                }
            }
            else
            {
                foreach (var __queryItem782 in __querySource782)
                {
                    if ((!typeof(Component).IsAssignableFrom(__queryItem782)))
                    {
                        __queryResult782 = true;
                        break;
                    }
                }
            }
        }
        
        if (__queryResult782)
        {
            throw new ArgumentException("Required types must derive from Component.");
        }
    }
}
