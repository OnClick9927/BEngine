namespace BEngine;

/// <summary>Preserves execution of a trusted, data-bounded kernel without editor loop instrumentation.
/// Use the fully qualified attribute name so the source instrumenter can identify it before compilation.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor)]
public sealed class SkipLoopGuardAttribute : Attribute;
