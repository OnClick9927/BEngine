namespace BEngine.PropertyAttributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class DropdownAttribute(params string[] choices) : ExtendedPropertyAttribute
{
    public IReadOnlyList<string> choices { get; } = choices ?? [];
    public string? providerMember { get; init; }

    public DropdownAttribute(string providerMember, bool useProvider) : this([])
    {
        if (!useProvider) throw new ArgumentException("useProvider must be true.", nameof(useProvider));
        this.providerMember = providerMember;
    }
}
