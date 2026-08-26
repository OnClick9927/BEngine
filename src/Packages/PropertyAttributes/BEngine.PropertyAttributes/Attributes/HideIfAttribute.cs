namespace BEngine.PropertyAttributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
public sealed class HideIfAttribute(string conditionMember, object? expectedValue = null)
    : ConditionalPropertyAttribute(conditionMember, expectedValue);
