using EditorCapabilities = BEngine.Editor.EditorServices;
using System.Diagnostics;
using System.Text.Json;
using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor.Build;

public static class PlayerContentUpdatePipeline
{
    public static Task<PlayerContentUpdateResult> BuildAsync(
        PlayerBuildRequest request,
        string outputDirectory,
        IProgress<PlayerBuildProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        EditorCapabilities.GetRequiredService<IPlayerBuildPipeline>()
            .BuildContentUpdateAsync(request, outputDirectory, progress, cancellationToken);

    internal static async Task<PlayerContentUpdateResult> BuildCoreAsync(
        PlayerBuildRequest request,
        string outputDirectory,
        IAssetBundleBuildPipeline assetBundleBuildPipeline,
        IProjectAssetDatabaseFactory assetDatabaseFactory,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        PlatformPlayerBuildCapabilities buildCapabilities,
        IProgress<PlayerBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(assetBundleBuildPipeline);
        ArgumentNullException.ThrowIfNull(assetDatabaseFactory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(platformService);
        ArgumentNullException.ThrowIfNull(buildCapabilities);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var projectPath = pathService.TrimEndingDirectorySeparator(
            pathService.GetFullPath(request.ProjectPath));
        var output = pathService.TrimEndingDirectorySeparator(
            pathService.GetFullPath(outputDirectory));
        ValidateLowercaseOutputDirectory(output, pathService);
        var workspace = ProjectWorkspace.Open(projectPath, fileSystem, pathService);
        ValidateOutput(workspace, output, pathService);
        var parent = pathService.GetDirectoryName(output) ??
                     throw new InvalidDataException($"Content output '{output}' has no parent directory.");
        fileSystem.CreateDirectory(parent);
        var buildRoot = pathService.Combine(
            parent, $".{pathService.GetFileName(output)}.{Guid.NewGuid():N}.content-build");
        var publishRoot = pathService.Combine(
            parent, $".{pathService.GetFileName(output)}.{Guid.NewGuid():N}.content-publish");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        PlayerBuildContext? context = null;
        var succeeded = false;
        try
        {
            var target = BEngine.Platform.BuildTargetUtility.Current.Get(request.TargetId);
            if (target.Platform == BuildTargetPlatform.Web && !Enum.IsDefined(request.WebScriptCompilation))
                throw new ArgumentOutOfRangeException(nameof(request.WebScriptCompilation));
            if (target.Platform == BuildTargetPlatform.Web && !Enum.IsDefined(request.WebFontBackend))
                throw new ArgumentOutOfRangeException(nameof(request.WebFontBackend));
            if (request.EncryptContent && target.Platform == BuildTargetPlatform.Web)
                throw new NotSupportedException("Content encryption currently requires a native Player; browser AES-GCM content loading is not supported.");
            context = new PlayerBuildContext(request, target, buildRoot, progress);
            fileSystem.CreateDirectory(buildRoot);
            progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.Preparing,
                $"Preparing content update for {target.TargetId}", 0));
            var version = await PlayerContentBuildPipeline.BuildHotUpdateAsync(
                    context, assetBundleBuildPipeline, assetDatabaseFactory, fileSystem, pathService,
                    buildCapabilities, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var layout = PlayerBuildLayout.Create(workspace, buildRoot, fileSystem, pathService);
            var bundles = pathService.Combine(layout.ResourceDirectory, "AssetBundles");
            if (!fileSystem.DirectoryExists(bundles) || !fileSystem.EnumerateFiles(
                    bundles, "latest.yaml", SearchOption.AllDirectories).Any())
                throw new InvalidDataException("Content update did not produce an AssetBundle release.");
            fileSystem.MoveDirectory(bundles, publishRoot);
            PlayerOutputValidation.ValidateLowercaseTree(publishRoot, fileSystem, pathService);
            var publication = PublishCore(
                publishRoot, output, fileSystem, pathService, platformService, cancellationToken);
            progress?.Report(new PlayerBuildProgress(
                PlayerBuildPhase.Completed,
                publication.Promoted
                    ? $"Published {version}; latest is now {publication.LatestVersion}"
                    : $"Published {version}; latest remains {publication.LatestVersion}",
                1));
            var result = new PlayerContentUpdateResult(output, version, timer.Elapsed)
            {
                LatestVersion = publication.LatestVersion,
                Promoted = publication.Promoted,
                ContentEncryptionKeyFingerprint = ((BEngine.Content.ContentEncryptionKey?)context.ContentEncryptionKey)?.Fingerprint
            };
            succeeded = true;
            return result;
        }
        finally
        {
            TryDeleteStrippingWorkDirectory(succeeded, context, workspace.RootPath,
                fileSystem, pathService, platformService);
            TryDeleteDirectory(buildRoot, fileSystem);
            TryDeleteDirectory(publishRoot, fileSystem);
        }
    }

    private static void ValidateOutput(
        ProjectWorkspace workspace,
        string output,
        PathUtility pathService)
    {
        var root = pathService.TrimEndingDirectorySeparator(workspace.RootPath);
        if (root.Equals(output, StringComparison.OrdinalIgnoreCase) ||
            root.StartsWith(
                output + pathService.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Content update output cannot contain the project directory.");
        var assets = pathService.TrimEndingDirectorySeparator(workspace.AssetsPath);
        if (output.Equals(assets, StringComparison.OrdinalIgnoreCase) ||
            output.StartsWith(
                assets + pathService.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Content updates cannot be built inside Assets.");
    }

    private static void ValidateLowercaseOutputDirectory(string output, PathUtility pathService)
    {
        var name = pathService.GetFileName(pathService.TrimEndingDirectorySeparator(output));
        if (!name.Equals(name.ToLowerInvariant(), StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Content update output directory names must be lowercase: '{name}'.");
    }

    public static void SetLatestVersion(
        string packageDirectory,
        string version,
        CancellationToken cancellationToken = default) =>
        EditorCapabilities.GetRequiredService<IPlayerBuildPipeline>()
            .SetLatestContentVersion(packageDirectory, version, cancellationToken);

    internal static void SetLatestVersionCore(
        string packageDirectory,
        string version,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(platformService);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        packageDirectory = pathService.TrimEndingDirectorySeparator(
            pathService.GetFullPath(packageDirectory));
        if (fileSystem.FileExists(packageDirectory))
            throw new IOException($"Content package is an existing file: '{packageDirectory}'.");
        if (!fileSystem.DirectoryExists(packageDirectory))
            throw new DirectoryNotFoundException(
                $"Content package directory was not found: '{packageDirectory}'.");

        var packageName = pathService.GetFileName(packageDirectory);
        if (string.IsNullOrWhiteSpace(packageName))
            throw new InvalidDataException(
                $"Content package directory has no package name: '{packageDirectory}'.");
        var output = pathService.GetDirectoryName(packageDirectory) ??
                     throw new InvalidDataException(
                         $"Content package directory has no parent: '{packageDirectory}'.");
        var normalizedVersion = BEngine.Player.ContentVersionLabel.NormalizeHotResourceVersion(version);

        using var publishLock = PlayerContentPublishLock.Acquire(
            output, packageName, fileSystem, pathService, platformService, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var target = ReadVersionPointer(
            packageDirectory,
            packageName,
            normalizedVersion,
            "selected",
            fileSystem,
            pathService);
        ValidatePublicationCatalog(
            target, packageName, fileSystem, pathService, cancellationToken);
        var pointerBytes = CreateLatestPointerBytes(target.Version);
        WriteLatestPointer(packageDirectory, pointerBytes, fileSystem, pathService, cancellationToken);

        var latestBytes = fileSystem.ReadAllBytes(pathService.Combine(packageDirectory, "latest.yaml"));
        if (!latestBytes.AsSpan().SequenceEqual(pointerBytes))
            throw new IOException(
                $"Content package '{packageName}' latest.yaml could not be verified after switching to " +
                $"'{normalizedVersion}'.");
    }

    internal static (string LatestVersion, bool Promoted) Publish(
        string staging,
        string output,
        CancellationToken cancellationToken = default) =>
        PublishCore(
            staging,
            output,
            EditorCapabilities.GetRequiredService<FileUtility>(),
            EditorCapabilities.GetRequiredService<PathUtility>(),
            EditorCapabilities.GetRequiredService<PlatformUtility>(),
            cancellationToken);

    internal static (string LatestVersion, bool Promoted) PublishCore(
        string staging,
        string output,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(platformService);
        ArgumentException.ThrowIfNullOrWhiteSpace(staging);
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        staging = pathService.GetFullPath(staging);
        output = pathService.GetFullPath(output);
        if (fileSystem.FileExists(output))
            throw new IOException($"Content update output is an existing file: '{output}'.");
        if (!fileSystem.DirectoryExists(staging))
            throw new DirectoryNotFoundException($"Content publication staging was not found: '{staging}'.");
        fileSystem.CreateDirectory(output);

        var sourcePackages = fileSystem.EnumerateDirectories(staging)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        if (sourcePackages.Length != 1)
            throw new InvalidDataException(
                $"A Player content publication must contain exactly one package, found {sourcePackages.Length}.");

        var sourcePackage = sourcePackages[0];
        cancellationToken.ThrowIfCancellationRequested();
        var packageName = pathService.GetFileName(sourcePackage);
        var source = ReadPublicationPointer(
            sourcePackage, packageName, "staged", fileSystem, pathService);
        ValidatePublicationCatalog(
            source, packageName, fileSystem, pathService, cancellationToken);
        using var publishLock = PlayerContentPublishLock.Acquire(
            output, packageName, fileSystem, pathService, platformService, cancellationToken);
        var destinationPackage = pathService.Combine(output, packageName);
        fileSystem.CreateDirectory(destinationPackage);
        var current = TryReadPublicationPointer(
            destinationPackage, packageName, "published", fileSystem, pathService);

        var destinationVersion = pathService.Combine(destinationPackage, source.Version.Version);
        if (!fileSystem.DirectoryExists(destinationVersion))
        {
            try { fileSystem.MoveDirectory(source.VersionDirectory, destinationVersion); }
            catch (IOException) when (fileSystem.DirectoryExists(destinationVersion))
            {
                if (!DirectoriesMatch(
                        source.VersionDirectory,
                        destinationVersion,
                        fileSystem,
                        pathService,
                        cancellationToken))
                    throw new InvalidDataException(
                        $"Published content version '{packageName}/{source.Version.Version}' is immutable and differs from " +
                        "the existing remote release.");
            }
        }
        else if (!DirectoriesMatch(
                     source.VersionDirectory,
                     destinationVersion,
                     fileSystem,
                     pathService,
                     cancellationToken))
            throw new InvalidDataException(
                $"Published content version '{packageName}/{source.Version.Version}' is immutable and differs from " +
                "the existing remote release.");

        var shouldPromote = current is null ||
                            BEngine.Player.ContentVersionLabel.CompareHotResourceVersions(
                                source.Version.Version, current.Version.Version) > 0;
        var selectedVersion = shouldPromote ? source.Version : current!.Version;
        var selectedPointerBytes = CreateLatestPointerBytes(selectedVersion);
        if (shouldPromote || current?.LatestBytes is null ||
            !current.LatestBytes.AsSpan().SequenceEqual(selectedPointerBytes))
            WriteLatestPointer(
                destinationPackage, selectedPointerBytes, fileSystem, pathService, cancellationToken);
        return (selectedVersion.Version, shouldPromote);
    }

    private static PublicationPointer? TryReadPublicationPointer(
        string packageDirectory,
        string packageName,
        string description,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        var latest = pathService.Combine(packageDirectory, "latest.yaml");
        return fileSystem.FileExists(latest)
            ? ReadPublicationPointer(
                packageDirectory, packageName, description, fileSystem, pathService)
            : null;
    }

    private static PublicationPointer ReadPublicationPointer(
        string packageDirectory,
        string packageName,
        string description,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        var latestPath = pathService.Combine(packageDirectory, "latest.yaml");
        if (!fileSystem.FileExists(latestPath))
            throw new InvalidDataException($"The {description} content package '{packageName}' has no latest.yaml.");
        var bytes = fileSystem.ReadAllBytes(latestPath);
        AssetBundleLatestPointer pointer;
        AssetBundleVersion? legacyVersion = null;
        try
        {
            pointer = AssetBundleCatalogSerializer.DeserializeLatestPointer(bytes);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or JsonException or NotSupportedException)
        {
            legacyVersion = AssetBundleCatalogSerializer.DeserializeVersion(bytes);
            pointer = new AssetBundleLatestPointer
            {
                Version = legacyVersion.Version
            };
        }
        var normalized = BEngine.Player.ContentVersionLabel.NormalizeHotResourceVersion(pointer.Version);
        if (!normalized.Equals(pointer.Version, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"The {description} Hot Resource version '{pointer.Version}' is not canonical.");
        var target = ReadVersionPointer(
            packageDirectory, packageName, normalized, description, fileSystem, pathService);
        if (legacyVersion is not null && !target.VersionBytes.AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException(
                $"The {description} latest.yaml does not match '{normalized}/version.yaml'.");
        return target with { LatestBytes = bytes };
    }

    private static PublicationPointer ReadVersionPointer(
        string packageDirectory,
        string packageName,
        string targetVersion,
        string description,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        var versionDirectory = pathService.Combine(packageDirectory, targetVersion);
        if (!fileSystem.DirectoryExists(versionDirectory))
            throw new DirectoryNotFoundException(
                $"The {description} content version directory was not found: " +
                $"'{packageName}/{targetVersion}'.");
        var versionPath = pathService.Combine(versionDirectory, "version.yaml");
        if (!fileSystem.FileExists(versionPath))
            throw new InvalidDataException(
                $"The {description} content version '{packageName}/{targetVersion}' has no version.yaml.");

        var bytes = fileSystem.ReadAllBytes(versionPath);
        var version = AssetBundleCatalogSerializer.DeserializeVersion(bytes);
        if (!version.PackageName.Equals(packageName, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"The {description} version package '{version.PackageName}' does not match '{packageName}'.");
        var normalized = BEngine.Player.ContentVersionLabel.NormalizeHotResourceVersion(version.Version);
        if (!normalized.Equals(targetVersion, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"The {description} version metadata targets '{version.Version}', not '{targetVersion}'.");
        return new PublicationPointer(version, bytes, versionDirectory);
    }

    private static byte[] CreateLatestPointerBytes(AssetBundleVersion version) =>
        AssetBundleCatalogSerializer.SerializeLatestPointer(new AssetBundleLatestPointer
        {
            Version = version.Version
        });

    private static void ValidatePublicationCatalog(
        PublicationPointer pointer,
        string packageName,
        FileUtility fileSystem,
        PathUtility pathService,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var relativeCatalog = AssetBundleValidation.NormalizeRelativePath(
            pointer.Version.CatalogFile, "published catalog file");
        var catalogPath = pathService.Combine(
            pointer.VersionDirectory,
            relativeCatalog.Replace('/', pathService.DirectorySeparatorChar));
        if (!fileSystem.FileExists(catalogPath))
            throw new InvalidDataException(
                $"The selected content version '{packageName}/{pointer.Version.Version}' has no catalog " +
                $"at '{relativeCatalog}'.");

        if (fileSystem.GetFileLength(catalogPath) != pointer.Version.CatalogSize)
            throw new InvalidDataException(
                $"The selected content catalog size does not match " +
                $"'{pointer.Version.Version}/version.yaml'.");
        var catalogBytes = fileSystem.ReadAllBytes(catalogPath);
        cancellationToken.ThrowIfCancellationRequested();
        var catalogHash = AssetBundleCatalogSerializer.ComputeSha256(catalogBytes);
        if (!catalogHash.Equals(pointer.Version.CatalogSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"The selected content catalog SHA256 does not match " +
                $"'{pointer.Version.Version}/version.yaml'.");

        var catalog = AssetBundleCatalogSerializer.DeserializeCatalog(catalogBytes);
        if (!catalog.PackageName.Equals(packageName, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"The selected content catalog package '{catalog.PackageName}' does not match '{packageName}'.");
        if (!catalog.Version.Equals(pointer.Version.Version, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"The selected content catalog version '{catalog.Version}' does not match " +
                $"'{pointer.Version.Version}'.");
    }

    private static void WriteLatestPointer(
        string packageDirectory,
        ReadOnlySpan<byte> bytes,
        FileUtility fileSystem,
        PathUtility pathService,
        CancellationToken cancellationToken)
    {
        var latestPath = pathService.Combine(packageDirectory, "latest.yaml");
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var temporary = pathService.Combine(
                packageDirectory, $".latest.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = fileSystem.OpenFile(
                           temporary,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                {
                    stream.Write(bytes);
                    fileSystem.Flush(stream, flushToDisk: true);
                }
                if (fileSystem.FileExists(latestPath))
                {
                    try
                    {
                        fileSystem.ReplaceFile(
                            temporary, latestPath, null, ignoreMetadataErrors: true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        fileSystem.MoveFile(temporary, latestPath, overwrite: true);
                    }
                }
                else fileSystem.MoveFile(temporary, latestPath);
                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException &&
                timer.Elapsed < TimeSpan.FromSeconds(2))
            {
                cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(25));
            }
            finally
            {
                TryDeleteFile(temporary, fileSystem);
            }
        }
    }

    private static bool DirectoriesMatch(
        string left,
        string right,
        FileUtility fileSystem,
        PathUtility pathService,
        CancellationToken cancellationToken)
    {
        var leftFiles = BuildIndex23766(fileSystem.EnumerateFiles(left, "*", SearchOption.AllDirectories), StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, string> BuildIndex23766(global::System.Collections.Generic.IEnumerable<string> indexSource23766, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer23766)
        {
            ArgumentNullException.ThrowIfNull(indexSource23766, "source");
            var knownCount = indexSource23766.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, string>(indexComparer23766);
            global::System.ReadOnlySpan<string> values;
            if (knownCount && indexSource23766 is string[] array)
                values = array;
            else if (knownCount && indexSource23766 is global::System.Collections.Generic.List<string> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string>(capacity, indexComparer23766);
                foreach (var item in indexSource23766)
                    result.Add(IndexKey23766(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string>(values.Length, indexComparer23766);
                foreach (var item in values)
                    result.Add(IndexKey23766(item), item);
                return result;
            }
        
            string IndexKey23766(string path) => pathService.GetRelativePath(left, path);
        }
        var rightFiles = BuildIndex23984(fileSystem.EnumerateFiles(right, "*", SearchOption.AllDirectories), StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, string> BuildIndex23984(global::System.Collections.Generic.IEnumerable<string> indexSource23984, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer23984)
        {
            ArgumentNullException.ThrowIfNull(indexSource23984, "source");
            var knownCount = indexSource23984.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, string>(indexComparer23984);
            global::System.ReadOnlySpan<string> values;
            if (knownCount && indexSource23984 is string[] array)
                values = array;
            else if (knownCount && indexSource23984 is global::System.Collections.Generic.List<string> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string>(capacity, indexComparer23984);
                foreach (var item in indexSource23984)
                    result.Add(IndexKey23984(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string>(values.Length, indexComparer23984);
                foreach (var item in values)
                    result.Add(IndexKey23984(item), item);
                return result;
            }
        
            string IndexKey23984(string path) => pathService.GetRelativePath(right, path);
        }
        if (leftFiles.Count != rightFiles.Count || Query24230())
            return false;
        bool Query24230()
        {
            var querySource24230 = leftFiles.Keys;
            ArgumentNullException.ThrowIfNull(querySource24230, "source");
            if (querySource24230.GetType() == typeof(string[]) || querySource24230.GetType() == typeof(global::System.Collections.Generic.List<string>))
            {
                global::System.ReadOnlySpan<string> queryValues24230 = querySource24230.GetType() == typeof(string[]) ? (string[])(object)querySource24230 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)(object)querySource24230);
                foreach (var path in queryValues24230)
                {
                    if (!rightFiles.ContainsKey(path))
                        return true;
                }
            }
            else
            {
                foreach (var path in querySource24230)
                {
                    if (!rightFiles.ContainsKey(path))
                        return true;
                }
            }
        
            return false;
        }
        foreach (var (relative, leftPath) in leftFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rightPath = rightFiles[relative];
            if (fileSystem.GetFileLength(leftPath) != fileSystem.GetFileLength(rightPath)) return false;
            using var leftStream = fileSystem.OpenRead(leftPath);
            using var rightStream = fileSystem.OpenRead(rightPath);
            if (!System.Security.Cryptography.SHA256.HashData(leftStream)
                    .SequenceEqual(System.Security.Cryptography.SHA256.HashData(rightStream))) return false;
        }
        return true;
    }

    private static void TryDeleteFile(string path, FileUtility fileSystem)
    {
        try
        {
            if (fileSystem.FileExists(path)) fileSystem.DeleteFile(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record PublicationPointer(
        AssetBundleVersion Version,
        byte[] VersionBytes,
        string VersionDirectory)
    {
        public byte[]? LatestBytes { get; init; }
    }

    private static void TryDeleteStrippingWorkDirectory(
        bool succeeded,
        PlayerBuildContext? context,
        string projectRoot,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService)
    {
        // Failed/cancelled passes retain their linker inputs and logs. A successful
        // request owns one GUID directory, never the shared PlayerStripping root
        // or another concurrent request's siblings.
        if (!succeeded || context?.StrippingWorkDirectory is not { } workDirectory) return;
        try
        {
            var parent = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(
                pathService.Combine(projectRoot, "Temp", "PlayerStripping")));
            var work = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(workDirectory));
            var comparison = platformService.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(pathService.GetDirectoryName(work), parent, comparison) ||
                !Guid.TryParseExact(pathService.GetFileName(work), "N", out _))
                throw new InvalidDataException($"Stripping work path is not a request directory below '{parent}': '{work}'.");
            if (!fileSystem.DirectoryExists(work)) return;
            if ((fileSystem.GetAttributes(work) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Stripping work path is a filesystem link: '{work}'.");
            fileSystem.DeleteDirectory(work, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            // Publication already succeeded. Cleanup must not turn it into a failed
            // update or cause a retry of an already published content version.
            Debug.LogWarning($"Published content; stripping intermediates remain at '{workDirectory}': {exception.Message}");
        }
    }

    private static void TryDeleteDirectory(string path, FileUtility fileSystem)
    {
        if (!fileSystem.DirectoryExists(path)) return;
        try { fileSystem.DeleteDirectory(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
