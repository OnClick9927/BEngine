namespace BEngine.PropertyAttributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class PasswordAttribute(char mask = '*') : ExtendedPropertyAttribute
{
    public char mask { get; } = mask;
}
