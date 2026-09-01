using BEngine.Serialization;

namespace BEngine;
/// <summary>Supplies logical resource content without exposing its storage implementation.</summary>
public interface IResourceProvider
{
    bool TryLoad(string path, string folderName, out ResourceContent content);

    IEnumerable<string> Enumerate(string path, string folderName);
}

/// <summary>
/// Optionally supplies fully constructed assets when raw resource bytes are not enough to preserve
/// importer settings or object identity.
/// </summary>
public interface IResourceAssetProvider
{
    bool TryLoadAsset(string path, string folderName, Type assetType, out BAsset asset);
}

public sealed record ResourceContent(string Path, byte[] Bytes);
internal static class ResourceLoader
{
    private static readonly HashSet<string> RegisteredRoots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<IResourceProvider> RegisteredProviders = [];

    internal static T? Load<T>(string path, string folderName) where T : class =>
        Load(path, typeof(T), folderName) as T;

    internal static object? Load(string path, Type type, string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (typeof(BAsset).IsAssignableFrom(type) && ResolveProviderAsset(path, folderName, type) is { } asset)
            return asset;
        var providerContent = ResolveProvider(path, folderName);
        if (providerContent is not null)
            return Decode(providerContent.Bytes, providerContent.Path, type);
        var file = Resolve(path, folderName);
        if (file is null) return null;
        return Decode(File.ReadAllBytes(file), file, type);
    }

    internal static BAsset? LoadAsset(string path, Type assetType, string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(assetType);
        if (!typeof(BAsset).IsAssignableFrom(assetType))
            throw new ArgumentException($"{assetType.FullName} is not a BAsset type.", nameof(assetType));
        return ResolveProviderAsset(path, folderName, assetType);
    }

    private static object Decode(byte[] bytes, string sourcePath, Type type)
    {
        if (type == typeof(byte[])) return (byte[])bytes.Clone();
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        if (type == typeof(string)) return text;
        if (type == typeof(PrefabAsset))
            return PrefabAssetSerialization.Deserialize(text, sourcePath);
        if (type == typeof(TextAsset) || type == typeof(BObject)) return new TextAsset(text, sourcePath);
        throw new NotSupportedException(
            $"Resource type '{type.FullName}' is not supported. Use TextAsset, string or byte[].");
    }

    internal static T[] LoadAll<T>(string path, string folderName) where T : class
    {
        var normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        var results = new List<T>();
        var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var providerPath in EnumerateProviders(path, folderName))
        {
            if (!loadedPaths.Add(NormalizeResourcePath(providerPath))) continue;
            try { if (Load<T>(providerPath, folderName) is { } asset) results.Add(asset); }
            catch (NotSupportedException) { }
        }
        foreach (var root in SearchRoots(folderName))
        {
            var directory = Path.Combine(root, normalized);
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(root, file);
                if (!loadedPaths.Add(NormalizeResourcePath(relativePath))) continue;
                try { if (Load<T>(relativePath, folderName) is { } asset) results.Add(asset); }
                catch (NotSupportedException) { }
            }
        }
        return results.ToArray();
    }

    internal static string? Resolve(string resourcePath, string folderName)
    {
        var normalized = resourcePath.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        if (normalized.Split(Path.DirectorySeparatorChar).Any(segment => segment == ".."))
            throw new ArgumentException("Resource paths cannot leave their resource root.", nameof(resourcePath));
        foreach (var root in SearchRoots(folderName))
        {
            var exact = Path.Combine(root, normalized);
            if (File.Exists(exact)) return exact;
            var directory = Path.GetDirectoryName(exact);
            if (directory is null || !Directory.Exists(directory)) continue;
            var match = Directory.EnumerateFiles(directory, $"{Path.GetFileName(exact)}.*")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (match is not null) return match;
        }
        return null;
    }

    internal static void RegisterRoot(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RegisteredRoots.Add(Path.GetFullPath(rootPath));
    }

    internal static bool UnregisterRoot(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        return RegisteredRoots.Remove(Path.GetFullPath(rootPath));
    }

    internal static void RegisterProvider(IResourceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!RegisteredProviders.Contains(provider)) RegisteredProviders.Insert(0, provider);
    }

    internal static bool UnregisterProvider(IResourceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return RegisteredProviders.Remove(provider);
    }

    private static ResourceContent? ResolveProvider(string path, string folderName)
    {
        var normalized = NormalizeResourcePath(path);
        foreach (var provider in RegisteredProviders.ToArray())
            if (provider.TryLoad(normalized, folderName, out var content)) return content;
        return null;
    }

    private static BAsset? ResolveProviderAsset(string path, string folderName, Type assetType)
    {
        var normalized = NormalizeResourcePath(path);
        foreach (var provider in RegisteredProviders.ToArray())
        {
            if (provider is not IResourceAssetProvider assetProvider ||
                !assetProvider.TryLoadAsset(normalized, folderName, assetType, out var asset)) continue;
            if (asset is null)
                throw new InvalidDataException(
                    $"Resource asset provider '{provider.GetType().FullName}' returned a null asset.");
            if (!assetType.IsInstanceOfType(asset))
                throw new InvalidDataException(
                    $"Resource asset provider '{provider.GetType().FullName}' returned " +
                    $"{asset.GetType().FullName}, expected {assetType.FullName}.");
            return asset;
        }
        return null;
    }

    private static IEnumerable<string> EnumerateProviders(string path, string folderName)
    {
        var normalized = NormalizeResourcePath(path);
        return RegisteredProviders.ToArray().SelectMany(provider => provider.Enumerate(normalized, folderName))
            .Select(NormalizeResourcePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeResourcePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim('/');
        if (normalized.Split('/').Any(segment => segment is ".." or "."))
            throw new ArgumentException("Resource paths cannot leave their resource root.", nameof(path));
        return normalized;
    }

    private static IEnumerable<string> SearchRoots(string folderName)
    {
        if (!string.IsNullOrWhiteSpace(Application.dataPath))
            yield return Path.Combine(Application.dataPath, folderName);
        yield return Path.Combine(AppContext.BaseDirectory, folderName);
        foreach (var root in RegisteredRoots.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray())
            yield return Path.Combine(root, folderName);
    }
}

public static class Resources
{
    public static T? Load<T>(string path) where T : class =>
        ResourceLoader.Load<T>(path, "Resources");

    public static object? Load(string path, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return ResourceLoader.Load(path, type, "Resources");
    }

    public static void RegisterResourceRoot(string rootPath) => ResourceLoader.RegisterRoot(rootPath);
    public static bool UnregisterResourceRoot(string rootPath) => ResourceLoader.UnregisterRoot(rootPath);
    public static void RegisterResourceProvider(IResourceProvider provider) => ResourceLoader.RegisterProvider(provider);
    public static bool UnregisterResourceProvider(IResourceProvider provider) => ResourceLoader.UnregisterProvider(provider);
    public static T[] LoadAll<T>(string path = "") where T : class =>
        ResourceLoader.LoadAll<T>(path, "Resources");

    public static void UnloadAsset(BObject assetToUnload)
    {
        if (assetToUnload is not null) BObject.DestroyImmediate(assetToUnload);
    }

    public static Task UnloadUnusedAssets()
    {
        GC.Collect();
        return Task.CompletedTask;
    }
}
