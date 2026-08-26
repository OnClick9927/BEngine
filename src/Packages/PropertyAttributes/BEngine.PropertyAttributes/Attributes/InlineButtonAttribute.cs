namespace BEngine.PropertyAttributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
public sealed class InlineButtonAttribute(string methodName, string? label = null) : ExtendedPropertyAttribute
{
    public string methodName { get; } = methodName;
    public string label { get; } = label ?? methodName;
}
