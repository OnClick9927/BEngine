namespace BEngine.Instrumentation;

/// <summary>Read-only object-registry observations; does not expose registry storage or mutable flags.</summary>
public static class RuntimeObjectDiagnostics
{
    public static int CountCurrentDomainObjects() => BObject.CountObjectsInCurrentRuntimeDomain();

    public static bool IsRuntimeOnly(BObject value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.IsRuntimeOnly;
    }
}
