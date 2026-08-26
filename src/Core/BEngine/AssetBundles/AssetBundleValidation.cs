namespace BEngine.AssetBundles;

internal static class AssetBundleValidation
{
    internal static void ValidateCatalog(AssetBundleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (!string.Equals(catalog.Format, AssetBundleCatalog.CurrentFormat, StringComparison.Ordinal) ||
            catalog.SchemaVersion is < AssetBundleCatalog.MinimumSupportedSchemaVersion or
                > AssetBundleCatalog.CurrentSchemaVersion)
            throw new InvalidDataException(
                $"Unsupported asset bundle catalog format '{catalog.Format}' version {catalog.SchemaVersion}.");
        ValidateIdentifier(catalog.PackageName, nameof(catalog.PackageName));
        ValidateVersionText(catalog.Version, nameof(catalog.Version));
        if (catalog.Bundles is null) throw new InvalidDataException("Catalog bundles cannot be null.");
        if (catalog.Assets is null) throw new InvalidDataException("Catalog assets cannot be null.");

        var bundles = new Dictionary<string, AssetBundleDescriptor>(StringComparer.OrdinalIgnoreCase);
        var bundleHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bundleFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bundle in catalog.Bundles)
        {
            if (bundle is null) throw new InvalidDataException("Catalog bundles cannot contain null entries.");
            ValidateIdentifier(bundle.Name, "bundle name");
            ValidateSha256(bundle.Sha256, $"bundle '{bundle.Name}' SHA256");
            if (bundle.Size <= 0) throw new InvalidDataException($"Bundle '{bundle.Name}' size must be positive.");
            var expectedFile = $"{bundle.Sha256.ToLowerInvariant()}.bassetbundle";
            if (!string.Equals(bundle.FileName, expectedFile, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(bundle.FileName) != bundle.FileName)
                throw new InvalidDataException(
                    $"Bundle '{bundle.Name}' file must be the content-addressed name '{expectedFile}'.");
            if (!bundles.TryAdd(bundle.Name, bundle))
                throw new InvalidDataException($"Duplicate bundle name '{bundle.Name}'.");
            if (!bundleHashes.Add(bundle.Sha256) || !bundleFiles.Add(bundle.FileName))
                throw new InvalidDataException(
                    $"Bundle '{bundle.Name}' reuses another bundle's content-addressed file.");
            if (bundle.Dependencies is null)
                throw new InvalidDataException($"Bundle '{bundle.Name}' dependencies cannot be null.");
            var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dependency in bundle.Dependencies)
            {
                ValidateIdentifier(dependency, $"dependency of '{bundle.Name}'");
                if (!dependencies.Add(dependency))
                    throw new InvalidDataException(
                        $"Bundle '{bundle.Name}' contains duplicate dependency '{dependency}'.");
                if (dependency.Equals(bundle.Name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Bundle '{bundle.Name}' cannot depend on itself.");
            }
        }

        foreach (var bundle in catalog.Bundles)
        foreach (var dependency in bundle.Dependencies)
            if (!bundles.ContainsKey(dependency))
                throw new InvalidDataException(
                    $"Bundle '{bundle.Name}' depends on missing bundle '{dependency}'.");
        ValidateAcyclic(bundles);

        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var guids = new HashSet<Guid>();
        foreach (var asset in catalog.Assets)
        {
            if (asset is null) throw new InvalidDataException("Catalog assets cannot contain null entries.");
            var address = NormalizeAddress(asset.Address);
            if (!string.Equals(address, asset.Address, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Asset address '{asset.Address}' is not canonical; expected '{address}'.");
            var entry = NormalizeAddress(asset.Entry);
            if (!string.Equals(entry, asset.Entry, StringComparison.Ordinal) ||
                !string.Equals(entry, address, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Asset '{asset.Address}' entry must be the canonical payload path '{address}'.");
            if (asset.Guid == Guid.Empty)
                throw new InvalidDataException($"Asset '{asset.Address}' has an empty GUID.");
            ValidateIdentifier(asset.Bundle, $"bundle of '{asset.Address}'");
            if (!bundles.ContainsKey(asset.Bundle))
                throw new InvalidDataException(
                    $"Asset '{asset.Address}' references missing bundle '{asset.Bundle}'.");
            ValidateType(asset.AssetType, asset.Address);
            ValidateImporter(asset);
            ValidateSha256(asset.Sha256, $"asset '{asset.Address}' SHA256");
            if (asset.Size < 0) throw new InvalidDataException($"Asset '{asset.Address}' size cannot be negative.");
            if (!addresses.Add(asset.Address))
                throw new InvalidDataException($"Duplicate asset address '{asset.Address}'.");
            if (!entries.Add(asset.Entry))
                throw new InvalidDataException($"Duplicate asset payload entry '{asset.Entry}'.");
            if (!guids.Add(asset.Guid))
                throw new InvalidDataException($"Duplicate asset GUID '{asset.Guid:D}'.");
        }
    }

    internal static void ValidateVersion(AssetBundleVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (!string.Equals(version.Format, AssetBundleVersion.CurrentFormat, StringComparison.Ordinal) ||
            version.SchemaVersion != AssetBundleVersion.CurrentSchemaVersion)
            throw new InvalidDataException(
                $"Unsupported asset bundle version format '{version.Format}' version {version.SchemaVersion}.");
        ValidateIdentifier(version.PackageName, nameof(version.PackageName));
        ValidateVersionText(version.Version, nameof(version.Version));
        var catalogFile = NormalizeRelativePath(version.CatalogFile, "catalog file");
        if (!string.Equals(catalogFile, version.CatalogFile, StringComparison.Ordinal) ||
            !catalogFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Catalog file '{version.CatalogFile}' must be a canonical relative JSON path.");
        ValidateSha256(version.CatalogSha256, nameof(version.CatalogSha256));
        if (version.CatalogSize <= 0)
            throw new InvalidDataException("Catalog size must be positive.");
    }

    internal static string NormalizeAddress(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        var normalized = address.Replace('\\', '/').Trim('/');
        if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["Assets/".Length..];
        normalized = NormalizeRelativePath(normalized, "asset address");
        return $"Assets/{normalized}";
    }

    internal static string NormalizeRelativePath(string path, string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path) || path.Contains('\0'))
            throw new InvalidDataException($"{fieldName} must be relative: '{path}'.");
        var normalized = path.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0)
            throw new InvalidDataException($"{fieldName} cannot be empty.");
        foreach (var segment in normalized.Split('/')) ValidatePathSegment(segment, fieldName);
        return normalized;
    }

    internal static void ValidateSha256(string value, string fieldName)
    {
        if (value is null || value.Length != 64 || value.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f') and not (>= 'A' and <= 'F')))
            throw new InvalidDataException($"{fieldName} must contain exactly 64 hexadecimal characters.");
    }

    private static void ValidateIdentifier(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            value[0] is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') ||
            value.Any(character => character is not (>= 'a' and <= 'z') and
                not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '.' and not '_' and not '-'))
            throw new InvalidDataException(
                $"{fieldName} must use 1-128 ASCII letters, digits, '.', '_' or '-'.");
    }

    private static void ValidateVersionText(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            value.Any(character => character > 127 || char.IsControl(character) ||
                char.IsWhiteSpace(character) || character is '/' or '\\' or ':'))
            throw new InvalidDataException($"{fieldName} is invalid.");
    }

    private static void ValidateType(string value, string address)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 ||
            value.Any(char.IsControl))
            throw new InvalidDataException($"Asset '{address}' has an invalid asset type.");
    }

    private static void ValidateImporter(AssetBundleAsset asset)
    {
        if (asset.Importer is null || asset.Importer.Length > 512 || asset.Importer.Any(char.IsControl))
            throw new InvalidDataException($"Asset '{asset.Address}' has an invalid importer name.");
        if (asset.ImporterSettings is null)
            throw new InvalidDataException($"Asset '{asset.Address}' importer settings cannot be null.");
        if (asset.ImporterSettings.Count > 128)
            throw new InvalidDataException($"Asset '{asset.Address}' has too many importer settings.");
        foreach (var (key, value) in asset.ImporterSettings)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(char.IsControl) ||
                value is null || value.Length > 2048 || value.Any(char.IsControl))
                throw new InvalidDataException($"Asset '{asset.Address}' has an invalid importer setting.");
        }
    }

    private static void ValidatePathSegment(string segment, string fieldName)
    {
        if (segment.Length == 0 || segment is "." or ".." || segment.Contains(':') ||
            segment.Any(character => char.IsControl(character) ||
                                     Array.IndexOf(Path.GetInvalidFileNameChars(), character) >= 0) ||
            !segment.Equals(segment.TrimEnd(' ', '.'), StringComparison.Ordinal) || IsReservedName(segment))
            throw new InvalidDataException($"{fieldName} contains an invalid path segment '{segment}'.");
    }

    private static bool IsReservedName(string segment)
    {
        var name = segment.Split('.')[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                                    name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
               name[3] is >= '1' and <= '9';
    }

    private static void ValidateAcyclic(IReadOnlyDictionary<string, AssetBundleDescriptor> bundles)
    {
        var states = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in bundles.Keys) Visit(name, bundles, states, []);
    }

    private static void Visit(
        string name,
        IReadOnlyDictionary<string, AssetBundleDescriptor> bundles,
        IDictionary<string, byte> states,
        List<string> path)
    {
        if (states.TryGetValue(name, out var state))
        {
            if (state == 2) return;
            if (state == 1)
                throw new InvalidDataException(
                    $"Asset bundle dependency cycle: {string.Join(" -> ", path.Append(name))}.");
        }
        states[name] = 1;
        path.Add(name);
        foreach (var dependency in bundles[name].Dependencies) Visit(dependency, bundles, states, path);
        path.RemoveAt(path.Count - 1);
        states[name] = 2;
    }
}
