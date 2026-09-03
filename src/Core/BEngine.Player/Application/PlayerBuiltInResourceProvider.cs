using BEngine.AssetBundles;
using BEngine.Content;

namespace BEngine.Player;

internal sealed class PlayerBuiltInResourceProvider : IResourceProvider, IDisposable
{
    private readonly BuiltInResourceArchive _archive;
    private readonly object _registrationGate = new();
    private int _registered;
    private int _disposed;

    internal PlayerBuiltInResourceProvider(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArchivePath = Path.GetFullPath(archivePath);
        _archive = BuiltInResourceArchive.Open(ArchivePath);
    }

    internal string ArchivePath { get; }
    internal IReadOnlyList<BuiltInResourceArchiveEntry> Entries => _archive.Entries;

    internal bool ContainsAddress(string address) => _archive.TryGetEntry(address, out _);

    internal IReadOnlyList<string> EnumerateAddresses(string prefix = "") =>
        _archive.EnumerateAddresses(prefix);

    internal byte[] ReadBytes(string address) => _archive.ReadBytes(address);

    internal BValueTask<byte[]> ReadBytesAsync(
        string address,
        CancellationToken cancellationToken = default) =>
        _archive.ReadBytesAsync(address, cancellationToken);

    internal void Activate()
    {
        lock (_registrationGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_registered != 0) return;
            Resources.RegisterResourceProvider(this);
            _registered = 1;
        }
    }

    internal void Deactivate()
    {
        lock (_registrationGate)
        {
            if (_registered == 0) return;
            Resources.UnregisterResourceProvider(this);
            _registered = 0;
        }
    }

    public bool TryLoad(string path, string folderName, out ResourceContent content)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var address = ResolveAddress(path, folderName);
        if (address is null)
        {
            content = null!;
            return false;
        }

        content = new ResourceContent(address, _archive.ReadBytes(address));
        return true;
    }

    public IEnumerable<string> Enumerate(string path, string folderName)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var requested = Normalize(path);
        if (requested.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
            requested.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            return _archive.EnumerateAddresses(requested);

        return ResourceEntries(folderName)
            .Where(item => requested.Length == 0 || item.ResourcePath.Equals(
                    requested, StringComparison.OrdinalIgnoreCase) ||
                item.ResourcePath.StartsWith(requested + "/", StringComparison.OrdinalIgnoreCase))
            .Select(static item => item.ResourcePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string? ResolveAddress(string path, string folderName)
    {
        var requested = Normalize(path);
        if (requested.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
            _archive.TryGetEntry(AssetBundleValidation.NormalizeAddress(requested), out var direct))
            return direct.Address;

        var matches = ResourceEntries(folderName)
            .Where(item => item.ResourcePath.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
                           MatchesWithoutExtension(item.ResourcePath, requested))
            .OrderBy(static item => item.Address, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0].Address,
            _ => throw new InvalidOperationException(
                $"Resource path '{path}' is ambiguous in the built-in AOT resource archive.")
        };
    }

    private IEnumerable<(string Address, string ResourcePath)> ResourceEntries(string folderName)
    {
        var marker = "/" + NormalizeFolder(folderName) + "/";
        foreach (var entry in _archive.Entries)
        {
            var index = entry.Address.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var relative = entry.Address[(index + marker.Length)..];
            if (relative.Length > 0) yield return (entry.Address, relative);
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
            throw new ArgumentException(
                "A resource folder name cannot contain path separators.", nameof(folderName));
        return normalized;
    }

    public void Dispose()
    {
        lock (_registrationGate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (_registered == 0) return;
            Resources.UnregisterResourceProvider(this);
            _registered = 0;
        }
    }
}
