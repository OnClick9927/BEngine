using BEngine.Serialization;

namespace BEngine;
internal static class ResourceLoader
{
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
        // File resources share identity with direct asset loads and are invalidated/unloaded by
        // the same lifecycle. Virtual provider bytes still use their own decoder path above.
        if (typeof(BAsset).IsAssignableFrom(type)) return BAsset.Load(file, type);
        return Decode(RuntimeServiceAccess.FileSystem.ReadAllBytes(file), file, type);
    }

    internal static async BTask<object?> LoadAsync(string path, Type type, string folderName, AsyncLoadContext context)
    {
        context.cancellationToken.ThrowIfCancellationRequested();
        await BTask.Yield();
        var normalized = NormalizeResourcePath(path);
        foreach (var provider in _registeredProviders.ToArray())
        {
            if (provider is not IAsyncResourceProvider asynchronous) continue;
            var result = await asynchronous.LoadAsync(normalized, folderName, type, context);
            context.cancellationToken.ThrowIfCancellationRequested();
            if (!result.Handled) continue;
            if (result.Asset is not null && !type.IsInstanceOfType(result.Asset))
                throw new InvalidDataException($"Async resource provider returned {result.Asset.GetType().FullName}, expected {type.FullName}.");
            return result.Asset;
        }
        if (typeof(BAsset).IsAssignableFrom(type) && RuntimeAssetCodecRegistry.HasPreparedDecoder(type))
        {
            if (ResolveProviderAsset(path, folderName, type) is { } provided) return provided;
            var providedBytes = ResolveProvider(path, folderName);
            if (providedBytes is not null)
            {
                var decoded = await RuntimeAssetCodecRegistry.DecodeAsync(providedBytes.Bytes, providedBytes.Path, type, context);
                if (decoded is not null && string.IsNullOrWhiteSpace(decoded.assetPath)) decoded.BindAssetReference(providedBytes.Path);
                return decoded;
            }
            var preparedFile = Resolve(path, folderName);
            if (preparedFile is not null && BAssetReferenceLoader.CanPrepareAsync(type))
                return await BAssetReferenceLoader.LoadPreparedAsync(preparedFile, type, context);
        }
        // Preserve BAsset identity and existing codecs. They can opt into the async SPI
        // above; legacy synchronous decoders remain one indivisible cooperative step.
        if (typeof(BAsset).IsAssignableFrom(type) || typeof(BObject).IsAssignableFrom(type))
        {
            object? asset = null;
            await context.RunSteps(() => { asset = Load(path, type, folderName); return CooperativeWorkStatus.Complete; });
            return asset;
        }
        ResourceContent? content = null;
        string? file = null;
        await context.RunSteps(() =>
        {
            content = ResolveProvider(path, folderName);
            if (content is null) file = Resolve(path, folderName);
            return CooperativeWorkStatus.Complete;
        });
        if (content is not null) return Decode(content.Bytes, content.Path, type);
        if (file is null) return null;
        using var stream = RuntimeServiceAccess.FileSystem.OpenRead(file);
        var bytes = await context.ReadAllBytesAsync(stream);
        context.cancellationToken.ThrowIfCancellationRequested();
        return type == typeof(byte[]) ? bytes : Decode(bytes, file, type);
    }

    internal static BAsset? LoadAsset(string path, Type assetType, string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(assetType);
        if (!typeof(BAsset).IsAssignableFrom(assetType))
            throw new ArgumentException($"{assetType.FullName} is not a BAsset type.", nameof(assetType));
        return ResolveProviderAsset(path, folderName, assetType);
    }

    private static object? Decode(byte[] bytes, string sourcePath, Type type)
    {
        if (type == typeof(byte[])) return (byte[])bytes.Clone();
        if (typeof(BAsset).IsAssignableFrom(type))
        {
            if (RuntimeAssetCodecRegistry.TryDecode(bytes, sourcePath, type, out var decoded))
            {
                if (string.IsNullOrWhiteSpace(decoded.assetPath)) decoded.BindAssetReference(sourcePath);
                return decoded;
            }
            if (type == typeof(Texture))
            {
                if (!Rendering.ImageCodec.TryGetInfo(bytes, out var width, out var height))
                {
                    if (Texture.IsSupportedSourcePath(sourcePath))
                        throw new InvalidDataException($"Resource Texture '{sourcePath}' is not a supported image.");
                    return null;
                }
                var texture = new Texture { width = width, height = height };
                texture.BindAssetReference(sourcePath);
                return texture;
            }
            if (RuntimeServiceAccess.FileSystem.FileExists(sourcePath) &&
                BAsset.Load(sourcePath, type) is { } asset) return asset;
        }
        var text = TextAsset.Decode(bytes);
        if (type == typeof(string)) return text;
        if (type == typeof(PrefabAsset))
            return PrefabAssetSerialization.Deserialize(text, sourcePath);
        if (type == typeof(TextAsset) || type == typeof(BObject)) return new TextAsset(text, sourcePath);
        throw new NotSupportedException(
            $"Resource type '{type.FullName}' is not supported. Use TextAsset, string or byte[].");
    }

    internal static T[] LoadAll<T>(string path, string folderName) where T : class
    {
        var fileSystem = RuntimeServiceAccess.FileSystem;
        var paths = RuntimeServiceAccess.PathService;
        var normalized = NormalizeResourcePath(path).Replace('/', paths.DirectorySeparatorChar);
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
            var directory = paths.Combine(root, normalized);
            if (!fileSystem.DirectoryExists(directory)) continue;
            foreach (var file in fileSystem.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var relativePath = paths.GetRelativePath(root, file);
                if (!loadedPaths.Add(NormalizeResourcePath(relativePath))) continue;
                try { if (Load<T>(relativePath, folderName) is { } asset) results.Add(asset); }
                catch (NotSupportedException) { }
            }
        }
        return results.ToArray();
    }

    internal static string? Resolve(string resourcePath, string folderName)
    {
        var fileSystem = RuntimeServiceAccess.FileSystem;
        var paths = RuntimeServiceAccess.PathService;
        var normalized = NormalizeResourcePath(resourcePath).Replace('/', paths.DirectorySeparatorChar);
        // Explicit project addresses are also valid outside a Resources folder.
        if (normalized.StartsWith("Assets" + paths.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Packages" + paths.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            var projectAsset = AssetReferencePath.Resolve(normalized);
            if (fileSystem.FileExists(projectAsset)) return projectAsset;
        }
        foreach (var root in SearchRoots(folderName))
        {
            var exact = paths.Combine(root, normalized);
            if (fileSystem.FileExists(exact)) return exact;
            var directory = paths.GetDirectoryName(exact);
            if (directory is null || !fileSystem.DirectoryExists(directory)) continue;
            var match = fileSystem.EnumerateFiles(directory, $"{paths.GetFileName(exact)}.*")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (match is not null) return match;
        }
        return null;
    }

    internal static void RegisterRoot(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var fullPath = RuntimeServiceAccess.PathService.GetFullPath(rootPath);
        if (_registeredRoots.Contains(fullPath, StringComparer.OrdinalIgnoreCase)) return;
        var roots = new string[_registeredRoots.Length + 1];
        _registeredRoots.CopyTo(roots, 0);
        roots[^1] = fullPath;
        Array.Sort(roots, StringComparer.OrdinalIgnoreCase);
        _registeredRoots = roots;
    }

    internal static bool UnregisterRoot(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var fullPath = RuntimeServiceAccess.PathService.GetFullPath(rootPath);
        var index = Array.FindIndex(_registeredRoots,
            candidate => candidate.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;
        var roots = new string[_registeredRoots.Length - 1];
        if (index > 0) Array.Copy(_registeredRoots, 0, roots, 0, index);
        if (index < roots.Length) Array.Copy(_registeredRoots, index + 1, roots, index, roots.Length - index);
        _registeredRoots = roots;
        return true;
    }

    internal static void RegisterProvider(IResourceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (_registeredProviders.Contains(provider)) return;
        var providers = new IResourceProvider[_registeredProviders.Length + 1];
        providers[0] = provider;
        _registeredProviders.CopyTo(providers, 1);
        _registeredProviders = providers;
        BAssetReferenceLoader.Clear();
    }

    internal static bool UnregisterProvider(IResourceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var index = Array.IndexOf(_registeredProviders, provider);
        if (index < 0) return false;
        var providers = new IResourceProvider[_registeredProviders.Length - 1];
        if (index > 0) Array.Copy(_registeredProviders, 0, providers, 0, index);
        if (index < providers.Length)
            Array.Copy(_registeredProviders, index + 1, providers, index, providers.Length - index);
        _registeredProviders = providers;
        BAssetReferenceLoader.Clear();
        return true;
    }

    private static ResourceContent? ResolveProvider(string path, string folderName)
    {
        var normalized = NormalizeResourcePath(path);
        foreach (var provider in _registeredProviders)
            if (provider.TryLoad(normalized, folderName, out var content)) return content;
        return null;
    }

    private static BAsset? ResolveProviderAsset(string path, string folderName, Type assetType)
    {
        var normalized = NormalizeResourcePath(path);
        foreach (var provider in _registeredProviders)
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
        foreach (var provider in _registeredProviders)
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
        return _registeredProviders
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
        if (RuntimeServiceAccess.PathService.IsPathRooted(candidate))
            throw new ArgumentException("Resource paths must be relative to their resource root.", nameof(path));
        var normalized = candidate.Replace('\\', '/').Trim('/');
        if (normalized.Split('/').Any(segment => segment is ".." or "."))
            throw new ArgumentException("Resource paths cannot leave their resource root.", nameof(path));
        return normalized;
    }

    private static IEnumerable<string> SearchRoots(string folderName)
    {
        if (!string.IsNullOrWhiteSpace(Application.dataPath))
            yield return RuntimeServiceAccess.PathService.Combine(Application.dataPath, folderName);
        yield return RuntimeServiceAccess.PathService.Combine(
            RuntimeServiceAccess.ApplicationPaths.BaseDirectory,
            folderName);
        foreach (var root in _registeredRoots)
            yield return RuntimeServiceAccess.PathService.Combine(root, folderName);
    }
}
