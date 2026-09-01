namespace BEngine.Editor;

public static class AssetTypeRegistry
{
    private static readonly Lock Sync = new();
    private static readonly Dictionary<string, Registration> Types =
        new(StringComparer.OrdinalIgnoreCase);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void Register(string fileSuffix, string displayName) =>
        Register(fileSuffix, displayName, null, null, null, null,
            System.Reflection.Assembly.GetCallingAssembly());

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void Register(string fileSuffix, string displayName, string iconResourcePath) =>
        Register(fileSuffix, displayName, iconResourcePath, null, null, null,
            System.Reflection.Assembly.GetCallingAssembly());

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void Register<TAsset>(
        string fileSuffix,
        string displayName,
        Func<AssetLoadContext, TAsset> loader,
        string? iconResourcePath = null,
        Type? importerType = null) where TAsset : BAsset
    {
        ArgumentNullException.ThrowIfNull(loader);
        Register(fileSuffix, displayName, iconResourcePath, typeof(TAsset), importerType,
            context => loader(context), System.Reflection.Assembly.GetCallingAssembly());
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void Register<TAsset>(
        string fileSuffix,
        string displayName,
        string? iconResourcePath = null,
        Type? importerType = null) where TAsset : BAsset =>
        Register(fileSuffix, displayName, iconResourcePath, typeof(TAsset), importerType, null,
            System.Reflection.Assembly.GetCallingAssembly());

    private static void Register(
        string fileSuffix,
        string displayName,
        string? iconResourcePath,
        Type? assetType,
        Type? importerType,
        Func<AssetLoadContext, BObject>? loader,
        System.Reflection.Assembly owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileSuffix);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (assetType is not null && !typeof(BAsset).IsAssignableFrom(assetType))
            throw new ArgumentException($"{assetType.FullName} is not a BAsset type.", nameof(assetType));
        if (importerType is not null && !typeof(AssetImporter).IsAssignableFrom(importerType))
            throw new ArgumentException($"{importerType.FullName} is not an AssetImporter type.",
                nameof(importerType));
        var suffix = NormalizeSuffix(fileSuffix);
        lock (Sync)
            Types[suffix] = new Registration(displayName, iconResourcePath, assetType, importerType, loader, owner);
    }

    public static bool Unregister(string fileSuffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileSuffix);
        var suffix = NormalizeSuffix(fileSuffix);
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

    public static Type? ResolveAssetType(string assetPath) => ResolveRegistration(assetPath)?.AssetType;

    public static Type? ResolveImporterType(string assetPath) => ResolveRegistration(assetPath)?.ImporterType;

    public static string? ResolveImporterName(string assetPath) =>
        ResolveImporterType(assetPath)?.Name;

    public static BObject? Load(AssetLoadContext context)
    {
        var registration = ResolveRegistration(context.SourcePath);
        if (registration is null) return null;
        var asset = registration.Loader?.Invoke(context) ??
                    (registration.AssetType is { } assetType && typeof(BAsset).IsAssignableFrom(assetType)
                        ? BAsset.Load(context.ImportedPath, assetType)
                        : null);
        if (asset is null) return null;
        if (registration.AssetType is { } expected && !expected.IsInstanceOfType(asset))
            throw new InvalidDataException(
                $"The asset loader for '{context.SourcePath}' returned {asset.GetType().FullName}, " +
                $"expected {expected.FullName}.");
        return asset;
    }

    public static string? ResolveIconPath(string assetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        lock (Sync)
        {
            var iconResourcePath = ResolveRegistrationUnchecked(assetPath)?.IconResourcePath;
            return string.IsNullOrWhiteSpace(iconResourcePath)
                ? null
                : EditorResource.FindPath(iconResourcePath) ?? iconResourcePath;
        }
    }

    internal static string? FindFileSuffix(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        lock (Sync)
            return Types.FirstOrDefault(pair => pair.Value.DisplayName.Equals(
                displayName, StringComparison.OrdinalIgnoreCase)).Key;
    }

    private static Registration? ResolveRegistration(string assetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        lock (Sync) return ResolveRegistrationUnchecked(assetPath);
    }

    private static Registration? ResolveRegistrationUnchecked(string assetPath) => Types
        .OrderByDescending(pair => pair.Key.Length)
        .FirstOrDefault(pair => assetPath.EndsWith(pair.Key, StringComparison.OrdinalIgnoreCase)).Value;

    private static string NormalizeSuffix(string fileSuffix) =>
        fileSuffix.StartsWith('.') ? fileSuffix : $".{fileSuffix}";

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
        Type? AssetType,
        Type? ImporterType,
        Func<AssetLoadContext, BObject>? Loader,
        System.Reflection.Assembly Owner);
}
