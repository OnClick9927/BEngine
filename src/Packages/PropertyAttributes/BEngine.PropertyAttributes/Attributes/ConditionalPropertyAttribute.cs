namespace BEngine.PropertyAttributes;

public abstract class ConditionalPropertyAttribute(string conditionMember, object? expectedValue = null)
    : ExtendedPropertyAttribute
{
    public string conditionMember { get; } = RequireName(conditionMember);
    public object? expectedValue { get; } = expectedValue;
    public bool hasExpectedValue { get; } = expectedValue is not null;

    private static string RequireName(string value) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("A condition member name is required.", nameof(value))
        : value;
}
