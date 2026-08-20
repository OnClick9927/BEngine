namespace BEngine.Documents;

public sealed class SortingLayerDocument : Document
{
    public ulong Value { get; set; }
    public string Name { get; set; } = string.Empty;

    public SortingLayerDefinition ToDefinition() => new(Value, Name);
}
