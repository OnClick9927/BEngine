namespace BEngine;

public static class RuntimePackageState
{
    private static readonly Dictionary<string, bool> States = new(StringComparer.OrdinalIgnoreCase);
    public static bool IsEnabled(string packageId) => string.IsNullOrWhiteSpace(packageId) ||
        !States.TryGetValue(packageId, out var enabled) || enabled;
    public static void SetEnabled(string packageId, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        States[packageId] = enabled;
    }
}
