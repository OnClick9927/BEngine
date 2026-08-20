namespace BEngine.PropertyAttributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class RequiredAttribute(string message = "This value is required.") : ExtendedPropertyAttribute
{
    public string message { get; } = message;
}
