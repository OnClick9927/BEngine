namespace BEngine.AssetBundles;

/// <summary>Exposes an active asset bundle catalog through the Resources API.</summary>
public sealed class AssetBundleResourceProvider : IResourceProvider
{
    private readonly IAssetBundleManager _manager;

    public AssetBundleResourceProvider(IAssetBundleManager manager) =>
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));

    public bool TryLoad(string path, string folderName, out ResourceContent content)
    {
        content = null!;
        if (!_manager.IsInitialized) return false;
        var address = ResolveAddress(path, folderName);
        if (address is null || !_manager.TryLoadBytes(address, out var bytes)) return false;
        content = new ResourceContent(address, bytes);
        return true;
    }

    public IEnumerable<string> Enumerate(string path, string folderName)
    {
        if (!_manager.IsInitialized) return [];
        var prefix = Normalize(path);
        return ResourceEntries(folderName)
            .Where(item => prefix.Length == 0 || item.ResourcePath.Equals(prefix,
                StringComparison.OrdinalIgnoreCase) || item.ResourcePath.StartsWith(prefix + "/",
                StringComparison.OrdinalIgnoreCase))
            .Select(item => item.ResourcePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string? ResolveAddress(string path, string folderName)
    {
        var requested = Normalize(path);
        var candidates = ResourceEntries(folderName).Where(item =>
                item.ResourcePath.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
                MatchesWithoutExtension(item.ResourcePath, requested))
            .OrderBy(item => item.Address, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        return candidates.Length switch
        {
            0 => null,
            1 => candidates[0].Address,
            _ => throw new InvalidOperationException(
                $"Resource path '{path}' is ambiguous in the active asset bundle catalog.")
        };
    }

    private IEnumerable<(string Address, string ResourcePath)> ResourceEntries(string folderName)
    {
        var marker = "/" + NormalizeFolder(folderName) + "/";
        foreach (var address in _manager.EnumerateAddresses())
        {
            var index = address.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var relative = address[(index + marker.Length)..];
            if (relative.Length > 0) yield return (address, relative);
        }
    }

    private static bool MatchesWithoutExtension(string candidate, string requested)
    {
        if (Path.HasExtension(requested) || candidate.Length <= requested.Length + 1 ||
            !candidate.StartsWith(requested, StringComparison.OrdinalIgnoreCase) ||
            candidate[requested.Length] != '.') return false;
        return !candidate.AsSpan(requested.Length + 1).Contains('/');
    }

    private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');

    private static string NormalizeFolder(string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        var normalized = Normalize(folderName);
        if (normalized.Contains('/'))
            throw new ArgumentException("A resource folder name cannot contain path separators.", nameof(folderName));
        return normalized;
    }
}
