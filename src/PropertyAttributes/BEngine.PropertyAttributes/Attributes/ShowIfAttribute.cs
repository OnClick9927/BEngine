namespace BEngine.PropertyAttributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
public sealed class ShowIfAttribute(string conditionMember, object? expectedValue = null)
    : ConditionalPropertyAttribute(conditionMember, expectedValue);
