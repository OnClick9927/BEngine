namespace BEngine;

public static class RuntimePackageState
{
    private static readonly Dictionary<string, bool> States = new(StringComparer.OrdinalIgnoreCase);
    internal static Dictionary<string, bool> Capture() => new(States, StringComparer.OrdinalIgnoreCase);
    internal static void Restore(IReadOnlyDictionary<string, bool> snapshot)
    {
        States.Clear();
        foreach (var pair in snapshot) States.Add(pair.Key, pair.Value);
    }
    public static bool IsEnabled(string packageId) => string.IsNullOrWhiteSpace(packageId) ||
        !States.TryGetValue(packageId, out var enabled) || enabled;
    public static void SetEnabled(string packageId, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        States[packageId] = enabled;
    }
}
