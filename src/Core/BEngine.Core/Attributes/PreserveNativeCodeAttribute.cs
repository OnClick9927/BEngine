namespace BEngine;

/// <summary>Retains the baseline native AOT profile for this interop boundary.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class PreserveNativeCodeAttribute : Attribute { }
