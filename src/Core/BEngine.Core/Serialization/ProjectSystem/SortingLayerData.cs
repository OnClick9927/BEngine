namespace BEngine.ProjectSystem;

public sealed class SortingLayerData
{
    public ulong Value { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool BuiltIn { get; set; }
    public bool IsUi { get; set; }
    public string BuiltInId { get; set; } = string.Empty;

    public LayerDefinition ToDefinition() => new(Value, Name, BuiltIn, IsUi, BuiltInId);
}
