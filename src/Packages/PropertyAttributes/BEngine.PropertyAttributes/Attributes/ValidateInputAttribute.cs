namespace BEngine.PropertyAttributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
public sealed class ValidateInputAttribute(string validatorMember, string message,
    ValidationMessageType messageType = ValidationMessageType.Error) : ExtendedPropertyAttribute
{
    public string validatorMember { get; } = validatorMember;
    public string message { get; } = message;
    public ValidationMessageType messageType { get; } = messageType;
}
