namespace BEngine;

public abstract class PropertyAttribute : Attribute
{
    public int order { get; set; }
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class SerializeFieldAttribute : Attribute;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HideInInspectorAttribute : Attribute;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class InspectorNameAttribute(string displayName) : PropertyAttribute
{
    public string displayName { get; } = displayName;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HeaderAttribute(string header) : PropertyAttribute
{
    public string header { get; } = header;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TooltipAttribute(string tooltip) : PropertyAttribute
{
    public string tooltip { get; } = tooltip;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class RangeAttribute(float min, float max) : PropertyAttribute
{
    public float min { get; } = min;
    public float max { get; } = max;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class MinAttribute(float min) : PropertyAttribute
{
    public float min { get; } = min;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class SpaceAttribute(float height = 8f) : PropertyAttribute
{
    public float height { get; } = height;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class MultilineAttribute(int lines = 3) : PropertyAttribute
{
    public int lines { get; } = lines;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TextAreaAttribute(int minLines = 3, int maxLines = 3) : PropertyAttribute
{
    public int minLines { get; } = minLines;
    public int maxLines { get; } = maxLines;
}

[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class DisallowMultipleComponentAttribute : Attribute;

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

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AddComponentMenuAttribute(string componentMenu, int componentOrder = 0) : Attribute
{
    public string componentMenu { get; } = componentMenu;
    public int componentOrder { get; } = componentOrder;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CreateAssetMenuAttribute : Attribute
{
    public string fileName { get; set; } = "New Scriptable Object";
    public string menuName { get; set; } = "";
    public int order { get; set; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class ContextMenuAttribute(string itemName) : Attribute
{
    public string itemName { get; } = itemName;
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class ExecuteAlwaysAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RuntimeInitializeOnLoadMethodAttribute(
    RuntimeInitializeLoadType loadType = RuntimeInitializeLoadType.AfterSceneLoad) : Attribute
{
    public RuntimeInitializeLoadType loadType { get; } = loadType;
}

public enum RuntimeInitializeLoadType
{
    AfterSceneLoad,
    BeforeSceneLoad,
    AfterAssembliesLoaded,
    BeforeSplashScreen,
    SubsystemRegistration
}
