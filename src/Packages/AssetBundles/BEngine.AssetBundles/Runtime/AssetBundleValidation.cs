namespace BEngine.AssetBundles;

internal static class AssetBundleValidation
{
    internal static void ValidateCatalog(AssetBundleCatalog catalog) => ValidateCatalogCore(catalog, false);

    // The caller must own this newly decoded catalog until its runtime index is
    // constructed. Seeds are never attached to publicly mutable catalog objects.
    internal static AssetBundleCatalogIndex.ValidatedSeeds ValidateOwnedCatalog(AssetBundleCatalog catalog,
        OwnedCatalogDecodeFacts? facts = null) => ValidateCatalogCore(catalog, true, facts)!;

    private static AssetBundleCatalogIndex.ValidatedSeeds? ValidateCatalogCore(
        AssetBundleCatalog catalog, bool retainIndexSeeds, OwnedCatalogDecodeFacts? facts = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var derivedEntries = facts?.Consume(catalog);
        using var measurement = AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.CatalogValidate);
        if (!string.Equals(catalog.Format, AssetBundleCatalog.CurrentFormat, StringComparison.Ordinal) ||
            catalog.SchemaVersion is < AssetBundleCatalog.MinimumSupportedSchemaVersion or
                > AssetBundleCatalog.CurrentSchemaVersion)
            throw new InvalidDataException(
                $"Unsupported asset bundle catalog format '{catalog.Format}' version {catalog.SchemaVersion}.");
        ValidateIdentifier(catalog.PackageName, nameof(catalog.PackageName));
        ValidateVersionText(catalog.Version, nameof(catalog.Version));
        if (catalog.Bundles is null) throw new InvalidDataException("Catalog bundles cannot be null.");
        if (catalog.Assets is null) throw new InvalidDataException("Catalog assets cannot be null.");
        if (catalog.Packages is null) throw new InvalidDataException("Catalog packages cannot be null.");
        if (catalog.SchemaVersion < 5 && catalog.Packages.Count > 0)
            throw new InvalidDataException("Explicit AssetBundle packages require catalog schema 5.");
        if (catalog.SchemaVersion < 5 && catalog.IsPackageComposite)
            throw new InvalidDataException("Composite package catalogs require catalog schema 5.");
        // Catalog traversal is bounded by its decoded records, including dependency lists.
        using var loopBudget = LoopExecutionGuard.AllowIterations(Math.Max(catalog.Bundles.Count, catalog.Assets.Count));

        var bundles = new Dictionary<string, AssetBundleDescriptor>(StringComparer.OrdinalIgnoreCase);
        var bundleHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bundleFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.CatalogBundleValidate))
        {
            foreach (var bundle in catalog.Bundles)
            {
                if (bundle is null) throw new InvalidDataException("Catalog bundles cannot contain null entries.");
                ValidateIdentifier(bundle.Name, "bundle name");
                if (facts is null) ValidateSha256(bundle.Sha256, $"bundle '{bundle.Name}' SHA256");
                if (bundle.SourceVersion is null)
                    throw new InvalidDataException($"Bundle '{bundle.Name}' source version cannot be null.");
                if (bundle.SourceVersion.Length > 0)
                    ValidateVersionText(bundle.SourceVersion, $"bundle '{bundle.Name}' source version");
                if (bundle.Size <= 0) throw new InvalidDataException($"Bundle '{bundle.Name}' size must be positive.");
                var expectedFile = facts is null ? $"{bundle.Sha256.ToLowerInvariant()}.bassetbundle" : bundle.FileName;
                if (facts is null && !string.Equals(bundle.FileName, expectedFile, StringComparison.OrdinalIgnoreCase))
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
        }
        var fingerprintBundles = retainIndexSeeds && catalog.Packages.Count > 0
            ? new Dictionary<string, OwnedPackageFingerprint.Input>(StringComparer.OrdinalIgnoreCase) : null;
        var fingerprintInputs = fingerprintBundles is null ? null : new List<OwnedPackageFingerprint.Input>();
        if (catalog.Packages.Count > 0)
        {
            var packageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var packageBundles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in catalog.Packages)
            {
                if (package is null) throw new InvalidDataException("Catalog packages cannot contain null entries.");
                ValidateIdentifier(package.Name, "assetbundle package name");
                ValidateSha256(package.Version, $"assetbundle package '{package.Name}' version");
                if (!packageNames.Add(package.Name))
                    throw new InvalidDataException($"Duplicate assetbundle package '{package.Name}'.");
                if (package.Bundles is null)
                    throw new InvalidDataException($"Assetbundle package '{package.Name}' bundles cannot be null.");
                var fingerprintInput = fingerprintInputs is null ? null : new OwnedPackageFingerprint.Input(facts is not null);
                if (fingerprintInput is not null) fingerprintInputs!.Add(fingerprintInput);
                foreach (var bundle in package.Bundles)
                {
                    ValidateIdentifier(bundle, $"bundle in assetbundle package '{package.Name}'");
                    if (!bundles.ContainsKey(bundle))
                        throw new InvalidDataException($"Assetbundle package '{package.Name}' references missing bundle '{bundle}'.");
                    if (!packageBundles.Add(bundle))
                        throw new InvalidDataException($"Bundle '{bundle}' must belong to exactly one assetbundle package.");
                    if (fingerprintInput is not null)
                    {
                        fingerprintInput.AddBundle(bundles[bundle]);
                        fingerprintBundles!.Add(bundle, fingerprintInput);
                    }
                }
            }
            if (packageBundles.Count != bundles.Count)
                throw new InvalidDataException("Every bundle must belong to an assetbundle package.");
        }

        var addresses = new Dictionary<string, AssetBundleAsset>(StringComparer.OrdinalIgnoreCase);
        var entries = new Dictionary<string, AssetBundleAsset>(StringComparer.OrdinalIgnoreCase);
        var assetsByGuid = new Dictionary<Guid, AssetBundleAsset>();
        // A main asset's identity is already its unique GUID. Only sub-assets
        // need a second key containing the local identifier.
        var subassets = new Dictionary<AssetIdentity, AssetBundleAsset>();
        var assetsByBundle = retainIndexSeeds
            ? new Dictionary<string, List<AssetBundleAsset>>(StringComparer.OrdinalIgnoreCase)
            : null;
        // Runtime catalogs intern their string table. Recheck scalar limits for
        // every record, but scan a shared text object only once in this call.
        // The bounded reference cache never outlives validation of mutable data.
        var checkedText = new HashSet<string>(ReferenceEqualityComparer.Instance);
        using (AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.CatalogAssetValidate))
        {
            var assetIndex = -1;
            foreach (var asset in catalog.Assets)
            {
                assetIndex++;
                if (asset is null) throw new InvalidDataException("Catalog assets cannot contain null entries.");
                var address = NormalizeAddress(asset.Address);
                if (!string.Equals(address, asset.Address, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"Asset address '{asset.Address}' is not canonical; expected '{address}'.");
                var supportsIndependentEntries = catalog.SchemaVersion >= 4;
                var entry = derivedEntries is not null && derivedEntries[assetIndex] ? asset.Entry :
                    NormalizePayloadEntry(asset.Entry, supportsIndependentEntries);
                if (!string.Equals(entry, asset.Entry, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"Asset '{asset.Address}' entry is not a canonical payload path.");
                if (asset.Guid == Guid.Empty)
                    throw new InvalidDataException($"Asset '{asset.Address}' has an empty GUID.");
                if (asset.LocalIdentifier < 0)
                    throw new InvalidDataException(
                        $"Asset '{asset.Address}' has a negative local identifier.");
                var ownerGuid = asset.OwnerGuid == Guid.Empty ? asset.Guid : asset.OwnerGuid;
                if (asset.LocalIdentifier == 0)
                {
                    if (ownerGuid != asset.Guid)
                        throw new InvalidDataException(
                            $"Main asset '{asset.Address}' owner GUID must match its asset GUID.");
                    if (IsSubAssetAddress(address))
                        throw new InvalidDataException(
                            $"Main asset '{asset.Address}' cannot use a sub-asset address.");
                    if (!supportsIndependentEntries && !string.Equals(entry, address, StringComparison.Ordinal))
                        throw new InvalidDataException(
                            $"Main asset '{asset.Address}' entry must be its canonical Assets path.");
                }
                else
                {
                    if (asset.OwnerGuid == Guid.Empty)
                        throw new InvalidDataException(
                            $"Sub-asset '{asset.Address}' has an empty owner GUID.");
                    var expectedAddress = CreateSubAssetAddress(ownerGuid, asset.LocalIdentifier);
                    if (!string.Equals(address, expectedAddress, StringComparison.Ordinal))
                        throw new InvalidDataException(
                            $"Sub-asset address '{asset.Address}' must be '{expectedAddress}'.");
                    if (!supportsIndependentEntries)
                    {
                        var extension = GetAssetExtension(entry);
                        string expectedEntry;
                        try { expectedEntry = CreateSubAssetEntry(ownerGuid, asset.LocalIdentifier, extension); }
                        catch (ArgumentException exception)
                        {
                            throw new InvalidDataException(
                                $"Sub-asset '{asset.Address}' has an invalid payload extension.", exception);
                        }
                        if (!string.Equals(entry, expectedEntry, StringComparison.Ordinal))
                            throw new InvalidDataException(
                                $"Sub-asset '{asset.Address}' entry must be '{expectedEntry}'.");
                    }
                }
                if (!IsValidIdentifier(asset.Bundle))
                    ValidateIdentifier(asset.Bundle, $"bundle of '{asset.Address}'");
                if (!bundles.ContainsKey(asset.Bundle))
                    throw new InvalidDataException(
                        $"Asset '{asset.Address}' references missing bundle '{asset.Bundle}'.");
                ValidateType(asset.AssetType, asset.Address, checkedText);
                var settingsOrdered = ValidateImporter(asset, checkedText, fingerprintBundles is not null);
                if (facts is null && !IsValidSha256(asset.Sha256))
                    ValidateSha256(asset.Sha256, $"asset '{asset.Address}' SHA256");
                if (asset.Size < 0) throw new InvalidDataException($"Asset '{asset.Address}' size cannot be negative.");
                if (!addresses.TryAdd(asset.Address, asset))
                    throw new InvalidDataException($"Duplicate asset address '{asset.Address}'.");
                var entryKey = supportsIndependentEntries
                    ? $"{asset.Bundle}\0{asset.Entry}"
                    : asset.Entry;
                if (entries.TryGetValue(entryKey, out var existingEntry))
                {
                    if (!supportsIndependentEntries ||
                        !existingEntry.Bundle.Equals(asset.Bundle, StringComparison.OrdinalIgnoreCase) ||
                        !existingEntry.Entry.Equals(asset.Entry, StringComparison.Ordinal) ||
                        !existingEntry.Sha256.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase) ||
                        existingEntry.Size != asset.Size)
                        throw new InvalidDataException($"Duplicate asset payload entry '{asset.Entry}'.");
                }
                else entries.Add(entryKey, asset);
                if (!assetsByGuid.TryAdd(asset.Guid, asset))
                    throw new InvalidDataException($"Duplicate asset GUID '{asset.Guid:D}'.");
                if (asset.LocalIdentifier != 0)
                {
                    var identity = new AssetIdentity(ownerGuid, asset.LocalIdentifier);
                    if (!subassets.TryAdd(identity, asset))
                        throw new InvalidDataException(
                            $"Duplicate asset identity '{CreateIdentityReference(identity)}'.");
                }
                if (assetsByBundle is not null)
                {
                    if (!assetsByBundle.TryGetValue(asset.Bundle, out var bundleAssets))
                        assetsByBundle.Add(asset.Bundle, bundleAssets = []);
                    bundleAssets.Add(asset);
                }
                if (fingerprintBundles is not null)
                    fingerprintBundles[asset.Bundle].AddAsset(asset, settingsOrdered);
            }

            foreach (var (identity, asset) in subassets)
            {
                if (!assetsByGuid.TryGetValue(identity.OwnerGuid, out var owner) || owner.LocalIdentifier != 0)
                    throw new InvalidDataException(
                        $"Sub-asset '{asset.Address}' references missing owner GUID '{identity.OwnerGuid:D}'.");
                if (!owner.Bundle.Equals(asset.Bundle, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"Sub-asset '{asset.Address}' must be stored in the same bundle as its owner.");
            }
        }
        using (AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.CatalogPackageFingerprint))
        {
            var fingerprintIndex = 0;
            foreach (var package in catalog.Packages)
                if (!package.Version.Equals(fingerprintInputs is null
                        ? AssetBundlePackageDescriptor.ComputeVersion(catalog, package.Bundles)
                        : OwnedPackageFingerprint.ComputeVersion(fingerprintInputs[fingerprintIndex++]),
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"Assetbundle package '{package.Name}' content fingerprint does not match its bundles and assets.");
        }
        if (!retainIndexSeeds) return null;
        // Validation is complete before publishing any seed. The remaining GUID
        // table is exactly the main-asset lookup the runtime index needs.
        foreach (var asset in subassets.Values) assetsByGuid.Remove(asset.Guid);
        return new AssetBundleCatalogIndex.ValidatedSeeds(catalog, addresses, assetsByGuid, assetsByBundle!, facts?.Canonical == true);
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
            (!catalogFile.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) &&
             !catalogFile.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(
                $"Catalog file '{version.CatalogFile}' must be a canonical relative YAML or binary catalog path.");
        ValidateSha256(version.CatalogSha256, nameof(version.CatalogSha256));
        if (version.CatalogSize <= 0)
            throw new InvalidDataException("Catalog size must be positive.");
    }

    internal static void ValidateLatestPointer(AssetBundleLatestPointer pointer)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        ValidateVersionText(pointer.Version, nameof(pointer.Version));
    }

    internal static string NormalizeAddress(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        if (address.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseSubAssetAddress(address, out var ownerGuid, out var localIdentifier))
                throw new InvalidDataException($"Invalid sub-asset address '{address}'.");
            return CreateSubAssetAddress(ownerGuid, localIdentifier);
        }
        if (address.StartsWith("Assets/", StringComparison.Ordinal) &&
            !address.Contains('\\') && address[^1] != '/')
        {
            ValidateRelativePath(address.AsSpan("Assets/".Length), "asset address");
            return address;
        }
        var normalized = address.Replace('\\', '/').Trim('/');
        if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["Assets/".Length..];
        normalized = NormalizeRelativePath(normalized, "asset address");
        return $"Assets/{normalized}";
    }

    internal static string CreateSubAssetAddress(Guid ownerGuid, long localIdentifier)
    {
        if (ownerGuid == Guid.Empty) throw new ArgumentException("An owner GUID is required.", nameof(ownerGuid));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(localIdentifier);
        return $"guid:{ownerGuid:N}#subasset={localIdentifier.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    internal static string CreateSubAssetEntry(Guid ownerGuid, long localIdentifier, string extension)
    {
        if (ownerGuid == Guid.Empty) throw new ArgumentException("An owner GUID is required.", nameof(ownerGuid));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(localIdentifier);
        extension = NormalizeArtifactExtension(extension);
        return $"Assets/__BEngineSubAssets/{ownerGuid:N}/" +
               $"{localIdentifier.ToString(System.Globalization.CultureInfo.InvariantCulture)}{extension}";
    }

    internal static string CreateOpaquePayloadEntry(string sha256)
    {
        ValidateSha256(sha256, "asset payload SHA256");
        return $"objects/{sha256.ToLowerInvariant()}.bin";
    }

    internal static bool TryParseSubAssetAddress(
        string address,
        out Guid ownerGuid,
        out long localIdentifier)
    {
        const string prefix = "guid:";
        const string separator = "#subasset=";
        ownerGuid = Guid.Empty;
        localIdentifier = 0;
        if (string.IsNullOrWhiteSpace(address) ||
            !address.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var separatorIndex = address.IndexOf(separator, prefix.Length, StringComparison.OrdinalIgnoreCase);
        return separatorIndex > prefix.Length &&
               Guid.TryParse(address.AsSpan(prefix.Length, separatorIndex - prefix.Length), out ownerGuid) &&
               long.TryParse(address.AsSpan(separatorIndex + separator.Length),
                   System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture, out localIdentifier) &&
               localIdentifier > 0;
    }

    private static string NormalizePayloadEntry(string entry, bool supportsIndependentEntries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry);
        if (entry.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Asset payload entries must be canonical relative paths.");
        var normalized = entry.Replace('\\', '/').Trim('/');
        if (supportsIndependentEntries)
            return NormalizeRelativePath(normalized, "asset payload entry");
        if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["Assets/".Length..];
        return $"Assets/{NormalizeRelativePath(normalized, "asset payload entry")}";
    }

    private static bool IsSubAssetAddress(string address) =>
        address.StartsWith("guid:", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeArtifactExtension(string extension)
    {
        extension = extension?.Trim() ?? string.Empty;
        if (!extension.StartsWith('.'))
            throw new ArgumentException("A payload file extension is required.", nameof(extension));
        if (extension.Length < 2 || extension.Skip(1).Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException($"Invalid payload file extension '{extension}'.", nameof(extension));
        return extension.ToLowerInvariant();
    }

    private static string CreateIdentityReference(AssetIdentity identity) =>
        identity.LocalIdentifier == 0
            ? $"guid:{identity.OwnerGuid:N}"
            : CreateSubAssetAddress(identity.OwnerGuid, identity.LocalIdentifier);

    internal static string NormalizeRelativePath(string path, string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (IsRootedLogicalPath(path) || path.Contains('\0'))
            throw new InvalidDataException($"{fieldName} must be relative: '{path}'.");
        var normalized = path.Replace('\\', '/').Trim('/');
        ValidateRelativePath(normalized.AsSpan(), fieldName);
        return normalized;
    }

    private static void ValidateRelativePath(ReadOnlySpan<char> path, string fieldName)
    {
        if (path.Length == 0) throw new InvalidDataException($"{fieldName} cannot be empty.");
        var start = 0;
        while (start < path.Length)
        {
            var separator = path[start..].IndexOf('/');
            if (separator < 0) { ValidatePathSegment(path[start..], fieldName); return; }
            ValidatePathSegment(path.Slice(start, separator), fieldName);
            start += separator + 1;
        }
        throw new InvalidDataException($"{fieldName} contains an empty path segment.");
    }

    internal static void ValidateSha256(string value, string fieldName)
    {
        if (!IsValidSha256(value))
            throw new InvalidDataException($"{fieldName} must contain exactly 64 hexadecimal characters.");
    }

    private static bool IsValidSha256(string? value) =>
        value is not null && value.Length == 64 && IsHexadecimal(value);

    private static bool IsHexadecimal(string value)
    {
        foreach (var character in value)
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f') and not (>= 'A' and <= 'F'))
                return false;
        return true;
    }

    private static void ValidateIdentifier(string value, string fieldName)
    {
        if (!IsValidIdentifier(value))
            throw new InvalidDataException(
                $"{fieldName} must use 1-128 ASCII letters, digits, '.', '_' or '-'.");
    }

    private static bool IsValidIdentifier(string value) =>
        !(string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            value[0] is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') ||
            value.Any(character => character is not (>= 'a' and <= 'z') and
                not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '.' and not '_' and not '-'));

    private static void ValidateVersionText(string value, string fieldName)
        => AssetBundleVersionLabel.ValidatePortable(value, fieldName);

    private static void ValidateType(string value, string address, HashSet<string> checkedText)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 ||
            ContainsControl(value, checkedText))
            throw new InvalidDataException($"Asset '{address}' has an invalid asset type.");
    }

    private static bool ValidateImporter(AssetBundleAsset asset, HashSet<string> checkedText, bool checkOrder)
    {
        if (asset.Importer is null || asset.Importer.Length > 512 || ContainsControl(asset.Importer, checkedText))
            throw new InvalidDataException($"Asset '{asset.Address}' has an invalid importer name.");
        if (asset.ImporterSettings is null)
            throw new InvalidDataException($"Asset '{asset.Address}' importer settings cannot be null.");
        if (asset.ImporterSettings.Count > 128)
            throw new InvalidDataException($"Asset '{asset.Address}' has too many importer settings.");
        var ordered = true;
        string? previousKey = null;
        foreach (var (key, value) in asset.ImporterSettings)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || ContainsControl(key, checkedText) ||
                value is null || value.Length > 2048 || ContainsControl(value, checkedText))
                throw new InvalidDataException($"Asset '{asset.Address}' has an invalid importer setting.");
            if (checkOrder)
            {
                if (previousKey is not null && StringComparer.Ordinal.Compare(previousKey, key) > 0) ordered = false;
                previousKey = key;
            }
        }
        return ordered;
    }

    private static bool ContainsControl(string value, HashSet<string> checkedText)
    {
        if (checkedText.Contains(value)) return false;
        foreach (var character in value)
            if (char.IsControl(character)) return true;
        if (checkedText.Count < 4096) checkedText.Add(value);
        return false;
    }

    private static void ValidatePathSegment(ReadOnlySpan<char> segment, string fieldName)
    {
        if (segment.Length == 0 || segment.SequenceEqual(".".AsSpan()) || segment.SequenceEqual("..".AsSpan()) ||
            segment[^1] is ' ' or '.' || IsReservedName(segment))
            throw new InvalidDataException($"{fieldName} contains an invalid path segment '{segment}'.");
        foreach (var character in segment)
            if (char.IsControl(character) || character is ':' or '<' or '>' or '"' or '|' or '?' or '*')
                throw new InvalidDataException($"{fieldName} contains an invalid path segment '{segment}'.");
    }

    private static string GetAssetExtension(string path)
    {
        var normalized = path.Replace('\\', '/');
        var fileName = normalized[(normalized.LastIndexOf('/') + 1)..];
        var extension = fileName.LastIndexOf('.');
        return extension <= 0 ? string.Empty : fileName[extension..];
    }

    private static bool IsRootedLogicalPath(string path) =>
        path[0] is '/' or '\\' ||
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';

    private static bool IsReservedName(ReadOnlySpan<char> segment)
    {
        var dot = segment.IndexOf('.');
        var name = dot < 0 ? segment : segment[..dot];
        return name.Equals("CON".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               name.Equals("PRN".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               name.Equals("AUX".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               name.Equals("NUL".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               name.Length == 4 && (name.StartsWith("COM".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                                    name.StartsWith("LPT".AsSpan(), StringComparison.OrdinalIgnoreCase)) &&
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

    private readonly record struct AssetIdentity(Guid OwnerGuid, long LocalIdentifier);
}
