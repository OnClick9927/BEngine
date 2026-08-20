namespace BEngine.Editor;

public static class AssetTypeRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Registration> Types =
        new(StringComparer.OrdinalIgnoreCase);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void Register(string fileSuffix, string displayName) =>
        Register(fileSuffix, displayName, null, System.Reflection.Assembly.GetCallingAssembly());

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void Register(string fileSuffix, string displayName, string iconResourcePath) =>
        Register(fileSuffix, displayName, iconResourcePath,
            System.Reflection.Assembly.GetCallingAssembly());

    private static void Register(
        string fileSuffix,
        string displayName,
        string? iconResourcePath,
        System.Reflection.Assembly owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileSuffix);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var suffix = fileSuffix.StartsWith('.') ? fileSuffix : $".{fileSuffix}";
        lock (Sync) Types[suffix] = new Registration(displayName, iconResourcePath, owner);
    }

    public static bool Unregister(string fileSuffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileSuffix);
        var suffix = fileSuffix.StartsWith('.') ? fileSuffix : $".{fileSuffix}";
        lock (Sync) return Types.Remove(suffix);
    }

    public static string? Resolve(string assetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        lock (Sync)
            return Types.OrderByDescending(pair => pair.Key.Length)
                .FirstOrDefault(pair => assetPath.EndsWith(
                    pair.Key, StringComparison.OrdinalIgnoreCase)).Value?.DisplayName;
    }

    public static string? ResolveIconPath(string assetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        lock (Sync)
        {
            var iconResourcePath = Types.OrderByDescending(pair => pair.Key.Length)
                .FirstOrDefault(pair => assetPath.EndsWith(
                    pair.Key, StringComparison.OrdinalIgnoreCase)).Value?.IconResourcePath;
            return string.IsNullOrWhiteSpace(iconResourcePath)
                ? null
                : EditorResources.FindPath(iconResourcePath) ?? iconResourcePath;
        }
    }

    internal static string? FindFileSuffix(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        lock (Sync)
            return Types.FirstOrDefault(pair => pair.Value.DisplayName.Equals(
                displayName, StringComparison.OrdinalIgnoreCase)).Key;
    }

    internal static void UnregisterAssembly(System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (Sync)
        {
            foreach (var suffix in Types.Where(pair => pair.Value.Owner == assembly)
                         .Select(pair => pair.Key).ToArray())
                Types.Remove(suffix);
        }
    }

    private sealed record Registration(
        string DisplayName,
        string? IconResourcePath,
        System.Reflection.Assembly Owner);
}
