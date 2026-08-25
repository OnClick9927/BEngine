namespace BEngine;

public readonly record struct SortingLayerDefinition(ulong Value, string Name)
{
    public int Index => SortingLayer.IndexOf(Value);
}
