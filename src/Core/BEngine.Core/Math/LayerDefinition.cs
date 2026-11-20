namespace BEngine;

public readonly record struct LayerDefinition(
    ulong Value,
    string Name,
    bool IsBuiltIn = false,
    bool IsUi = false,
    string BuiltInId = "")
{
    public int Index => SortingLayer.IndexOf(Value);
}
