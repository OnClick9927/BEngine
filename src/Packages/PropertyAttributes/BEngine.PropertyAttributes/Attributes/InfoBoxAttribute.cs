namespace BEngine.PropertyAttributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
public sealed class InfoBoxAttribute(string message, ValidationMessageType messageType = ValidationMessageType.Info,
    string? visibleIf = null) : ExtendedPropertyAttribute
{
    public string message { get; } = message;
    public ValidationMessageType messageType { get; } = messageType;
    public string? visibleIf { get; } = visibleIf;
}
