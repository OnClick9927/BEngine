using BEngine.Documents;

namespace BEngine;

internal static class ResourceLoader
{
    private static readonly object RootSync = new();
    private static readonly HashSet<string> RegisteredRoots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<IResourceProvider> RegisteredProviders = [];

    internal static T? Load<T>(string path, string folderName) where T : class =>
        Load(path, typeof(T), folderName) as T;

    internal static object? Load(string path, Type type, string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var providerContent = ResolveProvider(path, folderName);
        if (providerContent is not null)
            return Decode(providerContent.Bytes, providerContent.Path, type);
        var file = Resolve(path, folderName);
        if (file is null) return null;
        return Decode(File.ReadAllBytes(file), file, type);
    }

    private static object Decode(byte[] bytes, string sourcePath, Type type)
    {
        if (type == typeof(byte[])) return (byte[])bytes.Clone();
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        if (type == typeof(string)) return text;
        if (type == typeof(PrefabAsset))
            return Document.FromYaml<PrefabDocument>(text).ToBObject(new DocumentConversionContext(sourcePath));
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
        lock (RootSync) RegisteredRoots.Add(Path.GetFullPath(rootPath));
    }

    internal static bool UnregisterRoot(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        lock (RootSync) return RegisteredRoots.Remove(Path.GetFullPath(rootPath));
    }

    internal static void RegisterProvider(IResourceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (RootSync)
        {
            if (!RegisteredProviders.Contains(provider)) RegisteredProviders.Insert(0, provider);
        }
    }

    internal static bool UnregisterProvider(IResourceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (RootSync) return RegisteredProviders.Remove(provider);
    }

    private static ResourceContent? ResolveProvider(string path, string folderName)
    {
        var normalized = NormalizeResourcePath(path);
        IResourceProvider[] providers;
        lock (RootSync) providers = RegisteredProviders.ToArray();
        foreach (var provider in providers)
            if (provider.TryLoad(normalized, folderName, out var content)) return content;
        return null;
    }

    private static IEnumerable<string> EnumerateProviders(string path, string folderName)
    {
        var normalized = NormalizeResourcePath(path);
        IResourceProvider[] providers;
        lock (RootSync) providers = RegisteredProviders.ToArray();
        return providers.SelectMany(provider => provider.Enumerate(normalized, folderName))
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
        string[] registered;
        lock (RootSync) registered = RegisteredRoots.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var root in registered) yield return Path.Combine(root, folderName);
    }
}
