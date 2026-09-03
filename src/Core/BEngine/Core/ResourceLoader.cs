using BEngine.Serialization;

namespace BEngine;
internal static class ResourceLoader
{
    private static readonly Lock RegistryGate = new();
    private static string[] _registeredRoots = [];
    private static IResourceProvider[] _registeredProviders = [];

    internal static T? Load<T>(string path, string folderName) where T : class =>
        Load(path, typeof(T), folderName) as T;

    internal static object? Load(string path, Type type, string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (typeof(BAsset).IsAssignableFrom(type) && ResolveProviderAsset(path, folderName, type) is { } asset)
            return asset;
        if (typeof(BObject).IsAssignableFrom(type) && ResolveProviderObject(path, folderName, type) is { } value)
            return value;
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
        if (typeof(BAsset).IsAssignableFrom(type))
        {
            if (RuntimeAssetCodecRegistry.TryDecode(bytes, sourcePath, type, out var decoded)) return decoded;
            if (File.Exists(sourcePath) && BAsset.Load(sourcePath, type) is { } asset) return asset;
        }
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
        var normalized = NormalizeResourcePath(path).Replace('/', Path.DirectorySeparatorChar);
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
        var normalized = NormalizeResourcePath(resourcePath).Replace('/', Path.DirectorySeparatorChar);
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
        var fullPath = Path.GetFullPath(rootPath);
        lock (RegistryGate)
        {
            if (_registeredRoots.Contains(fullPath, StringComparer.OrdinalIgnoreCase)) return;
            var roots = new string[_registeredRoots.Length + 1];
            _registeredRoots.CopyTo(roots, 0);
            roots[^1] = fullPath;
            Array.Sort(roots, StringComparer.OrdinalIgnoreCase);
            Volatile.Write(ref _registeredRoots, roots);
        }
    }

    internal static bool UnregisterRoot(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var fullPath = Path.GetFullPath(rootPath);
        lock (RegistryGate)
        {
            var index = Array.FindIndex(_registeredRoots,
                candidate => candidate.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return false;
            var roots = new string[_registeredRoots.Length - 1];
            if (index > 0) Array.Copy(_registeredRoots, 0, roots, 0, index);
            if (index < roots.Length) Array.Copy(_registeredRoots, index + 1, roots, index, roots.Length - index);
            Volatile.Write(ref _registeredRoots, roots);
            return true;
        }
    }

    internal static void RegisterProvider(IResourceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (RegistryGate)
        {
            if (_registeredProviders.Contains(provider)) return;
            var providers = new IResourceProvider[_registeredProviders.Length + 1];
            providers[0] = provider;
            _registeredProviders.CopyTo(providers, 1);
            Volatile.Write(ref _registeredProviders, providers);
        }
    }

    internal static bool UnregisterProvider(IResourceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (RegistryGate)
        {
            var index = Array.IndexOf(_registeredProviders, provider);
            if (index < 0) return false;
            var providers = new IResourceProvider[_registeredProviders.Length - 1];
            if (index > 0) Array.Copy(_registeredProviders, 0, providers, 0, index);
            if (index < providers.Length)
                Array.Copy(_registeredProviders, index + 1, providers, index, providers.Length - index);
            Volatile.Write(ref _registeredProviders, providers);
            return true;
        }
    }

    private static ResourceContent? ResolveProvider(string path, string folderName)
    {
        var normalized = NormalizeResourcePath(path);
        foreach (var provider in Volatile.Read(ref _registeredProviders))
            if (provider.TryLoad(normalized, folderName, out var content)) return content;
        return null;
    }

    private static BAsset? ResolveProviderAsset(string path, string folderName, Type assetType)
    {
        var normalized = NormalizeResourcePath(path);
        foreach (var provider in Volatile.Read(ref _registeredProviders))
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

    private static BObject? ResolveProviderObject(string path, string folderName, Type objectType)
    {
        var normalized = NormalizeResourcePath(path);
        foreach (var provider in Volatile.Read(ref _registeredProviders))
        {
            if (provider is not IResourceObjectProvider objectProvider ||
                !objectProvider.TryLoadObject(normalized, folderName, objectType, out var value)) continue;
            if (value is null)
                throw new InvalidDataException(
                    $"Resource object provider '{provider.GetType().FullName}' returned a null object.");
            if (!objectType.IsInstanceOfType(value))
                throw new InvalidDataException(
                    $"Resource object provider '{provider.GetType().FullName}' returned " +
                    $"{value.GetType().FullName}, expected {objectType.FullName}.");
            return value;
        }
        return null;
    }

    private static IEnumerable<string> EnumerateProviders(string path, string folderName)
    {
        var normalized = NormalizeResourcePath(path);
        return Volatile.Read(ref _registeredProviders)
            .SelectMany(provider => provider.Enumerate(normalized, folderName))
            .Select(NormalizeResourcePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeResourcePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var candidate = path.Trim();
        if (Path.IsPathRooted(candidate))
            throw new ArgumentException("Resource paths must be relative to their resource root.", nameof(path));
        var normalized = candidate.Replace('\\', '/').Trim('/');
        if (normalized.Split('/').Any(segment => segment is ".." or "."))
            throw new ArgumentException("Resource paths cannot leave their resource root.", nameof(path));
        return normalized;
    }

    private static IEnumerable<string> SearchRoots(string folderName)
    {
        if (!string.IsNullOrWhiteSpace(Application.dataPath))
            yield return Path.Combine(Application.dataPath, folderName);
        yield return Path.Combine(AppContext.BaseDirectory, folderName);
        foreach (var root in Volatile.Read(ref _registeredRoots))
            yield return Path.Combine(root, folderName);
    }
}
