using BEngine.Platform;

namespace BEngine.Editor.Build;

/// <summary>A build-local snapshot of code literals and selected textual resources.</summary>
public sealed class PlayerResourceReferenceSet
{
    private readonly HashSet<string> _literals;
    private readonly HashSet<Guid> _guids;
    private readonly string[] _texts;

    private PlayerResourceReferenceSet(HashSet<string> literals, string[] texts)
    {
        _literals = literals;
        _texts = texts;
        _guids = [];
        foreach (var literal in literals)
        {
            var value = literal.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) ? literal[5..] : literal;
            var separator = value.IndexOf('#');
            if (separator >= 0) value = value[..separator];
            if (Guid.TryParse(value, out var guid)) _guids.Add(guid);
        }
    }

    public static PlayerResourceReferenceSet Read(IEnumerable<string> assemblyPaths,
        IEnumerable<string>? referencedFiles = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assemblyPaths);
        cancellationToken.ThrowIfCancellationRequested();
        var literals = PlayerManagedCodeTrimmer.GetStringConstants(CheckedPaths());
        var texts = new List<string>();
        foreach (var path in referencedFiles ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PlayerManagedCodeTrimmer.IsTextAsset(path)) texts.Add(FileUtility.Current.ReadAllText(path));
        }
        return new(literals, texts.ToArray());

        IEnumerable<string> CheckedPaths()
        {
            foreach (var path in assemblyPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return path;
            }
        }
    }

    /// <summary>Tests an asset address and optional provider-owned aliases and directory-literal scope.</summary>
    public bool ReferencesAssetPath(string address, string? resourceRelativePath = null,
        string? directoryLiteralPrefix = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        address = address.Replace('\\', '/');
        if (_literals.Contains(address) || _texts.Any(text => text.Contains(address, StringComparison.OrdinalIgnoreCase)))
            return true;
        if (directoryLiteralPrefix is not null && _literals.Any(value =>
                value.StartsWith(directoryLiteralPrefix, StringComparison.OrdinalIgnoreCase) && IsWithin(address, value)))
            return true;
        if (resourceRelativePath is null) return false;
        var alias = resourceRelativePath.Replace('\\', '/');
        var name = Path.GetFileName(alias);
        var dot = name.IndexOf('.');
        var stem = alias[..(alias.LastIndexOf('/') + 1)] + (dot < 0 ? name : name[..dot]);
        return _literals.Contains(alias) || _literals.Contains(stem) || _literals.Any(value => IsWithin(alias, value));
    }

    public bool ReferencesGuid(Guid guid) => _guids.Contains(guid) || _texts.Any(text =>
        text.Contains(guid.ToString("N"), StringComparison.OrdinalIgnoreCase) ||
        text.Contains(guid.ToString("D"), StringComparison.OrdinalIgnoreCase));

    /// <summary>Follows references in retained text files until the selected resource set is stable.</summary>
    public IReadOnlyList<RuntimePackageResourceInput> SelectResources(
        IReadOnlyList<RuntimePackageResourceInput> availableResources, IEnumerable<string> preservedRoots,
        PlayerStrippingReport report, Func<RuntimePackageResourceInput, bool>? requiredRoot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(availableResources);
        ArgumentNullException.ThrowIfNull(preservedRoots);
        ArgumentNullException.ThrowIfNull(report);
        cancellationToken.ThrowIfCancellationRequested();
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // A selection must not mutate this snapshot: another content partition may reuse it.
        var texts = new List<string>(_texts);
        foreach (var resource in availableResources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (requiredRoot?.Invoke(resource) == true) Include(resource);
        }
        foreach (var preserved in preservedRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matched = false;
            foreach (var resource in availableResources)
                if (IsWithin(resource.Address, preserved)) { Include(resource); matched = true; }
            if (!matched) throw new InvalidDataException($"Preserved package resource was not found: '{preserved}'.");
        }
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var resource in availableResources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (included.Contains(resource.Address)) continue;
                var path = resource.Address.Replace('\\', '/');
                var name = Path.GetFileName(path);
                var dot = name.IndexOf('.');
                var stem = dot < 0 ? name : name[..dot];
                if (!_literals.Any(value => path.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith('/' + value.TrimStart('/'), StringComparison.OrdinalIgnoreCase) ||
                        path.Contains('/' + value.Trim('/') + '.', StringComparison.OrdinalIgnoreCase) ||
                        name.Equals(value, StringComparison.OrdinalIgnoreCase) || stem.Equals(value, StringComparison.OrdinalIgnoreCase)) &&
                    !texts.Any(text => text.Contains(path, StringComparison.OrdinalIgnoreCase) ||
                                       text.Contains(name, StringComparison.OrdinalIgnoreCase))) continue;
                Include(resource);
                changed = true;
            }
        }
        foreach (var resource in availableResources)
        {
            var kept = included.Contains(resource.Address);
            report.Resources.Add(new PlayerStrippingReportEntry
            {
                Name = resource.Address, OriginalBytes = resource.Size,
                PublishedBytes = kept ? resource.Size : 0, Included = kept,
                Reason = kept ? "Resource reference, runtime literal or explicit preservation" : "No reachable resource reference"
            });
        }
        return availableResources.Where(resource => included.Contains(resource.Address)).ToArray();

        void Include(RuntimePackageResourceInput resource)
        {
            if (!included.Add(resource.Address)) return;
            if (PlayerManagedCodeTrimmer.IsTextAsset(resource.FilePath)) texts.Add(FileUtility.Current.ReadAllText(resource.FilePath));
        }
    }

    private static bool IsWithin(string value, string root) =>
        value.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith(root.TrimEnd('/') + '/', StringComparison.OrdinalIgnoreCase);
}
