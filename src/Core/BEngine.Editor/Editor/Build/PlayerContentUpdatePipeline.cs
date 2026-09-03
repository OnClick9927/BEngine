using System.Diagnostics;
using System.Text.Json;
using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.ProjectSystem;

namespace BEngine.Editor;

public static class PlayerContentUpdatePipeline
{
    public static async Task<PlayerContentUpdateResult> BuildAsync(
        PlayerBuildRequest request,
        string outputDirectory,
        IProgress<PlayerBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var projectPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.ProjectPath));
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        ValidateLowercaseOutputDirectory(output);
        var workspace = ProjectWorkspace.Open(projectPath);
        ValidateOutput(workspace, output);
        var parent = Path.GetDirectoryName(output) ??
                     throw new InvalidDataException($"Content output '{output}' has no parent directory.");
        Directory.CreateDirectory(parent);
        var buildRoot = Path.Combine(parent, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.content-build");
        var publishRoot = Path.Combine(parent, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.content-publish");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var target = BuildTargetCatalog.Get(request.TargetId);
            var context = new PlayerBuildContext(request, target, buildRoot, progress);
            Directory.CreateDirectory(buildRoot);
            progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.Preparing,
                $"Preparing content update for {target.TargetId}", 0));
            var version = await PlayerContentBuildPipeline.BuildHotUpdateAsync(context, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var layout = PlayerBuildLayout.Create(workspace, buildRoot);
            var bundles = Path.Combine(layout.ResourceDirectory, "AssetBundles");
            if (!Directory.Exists(bundles) || !Directory.EnumerateFiles(
                    bundles, "latest.json", SearchOption.AllDirectories).Any())
                throw new InvalidDataException("Content update did not produce an AssetBundle release.");
            Directory.Move(bundles, publishRoot);
            PlayerBuildLayout.ValidateLowercaseOutputTree(publishRoot);
            var publication = Publish(publishRoot, output, cancellationToken);
            progress?.Report(new PlayerBuildProgress(
                PlayerBuildPhase.Completed,
                publication.Promoted
                    ? $"Published {version}; latest is now {publication.LatestVersion}"
                    : $"Published {version}; latest remains {publication.LatestVersion}",
                1));
            return new PlayerContentUpdateResult(output, version, timer.Elapsed)
            {
                LatestVersion = publication.LatestVersion,
                Promoted = publication.Promoted
            };
        }
        finally
        {
            TryDeleteDirectory(buildRoot);
            TryDeleteDirectory(publishRoot);
        }
    }

    private static void ValidateOutput(ProjectWorkspace workspace, string output)
    {
        var root = Path.TrimEndingDirectorySeparator(workspace.RootPath);
        if (root.Equals(output, StringComparison.OrdinalIgnoreCase) ||
            root.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Content update output cannot contain the project directory.");
        var assets = Path.TrimEndingDirectorySeparator(workspace.AssetsPath);
        if (output.Equals(assets, StringComparison.OrdinalIgnoreCase) ||
            output.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Content updates cannot be built inside Assets.");
    }

    private static void ValidateLowercaseOutputDirectory(string output)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(output));
        if (!name.Equals(name.ToLowerInvariant(), StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Content update output directory names must be lowercase: '{name}'.");
    }

    public static void SetLatestVersion(
        string packageDirectory,
        string version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        packageDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageDirectory));
        if (File.Exists(packageDirectory))
            throw new IOException($"Content package is an existing file: '{packageDirectory}'.");
        if (!Directory.Exists(packageDirectory))
            throw new DirectoryNotFoundException(
                $"Content package directory was not found: '{packageDirectory}'.");

        var packageName = Path.GetFileName(packageDirectory);
        if (string.IsNullOrWhiteSpace(packageName))
            throw new InvalidDataException(
                $"Content package directory has no package name: '{packageDirectory}'.");
        var output = Path.GetDirectoryName(packageDirectory) ??
                     throw new InvalidDataException(
                         $"Content package directory has no parent: '{packageDirectory}'.");
        var normalizedVersion = PlayerBuildSettingsStore.NormalizeHotResourceVersion(version);

        using var publishLock = PlayerContentPublishLock.Acquire(
            output, packageName, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var target = ReadVersionPointer(
            packageDirectory, packageName, normalizedVersion, "selected");
        ValidatePublicationCatalog(target, packageName, cancellationToken);
        var pointerBytes = CreateLatestPointerBytes(target.Version);
        WriteLatestPointer(packageDirectory, pointerBytes, cancellationToken);

        var latestBytes = File.ReadAllBytes(Path.Combine(packageDirectory, "latest.json"));
        if (!latestBytes.AsSpan().SequenceEqual(pointerBytes))
            throw new IOException(
                $"Content package '{packageName}' latest.json could not be verified after switching to " +
                $"'{normalizedVersion}'.");
    }

    internal static (string LatestVersion, bool Promoted) Publish(
        string staging,
        string output,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(staging);
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        staging = Path.GetFullPath(staging);
        output = Path.GetFullPath(output);
        if (File.Exists(output))
            throw new IOException($"Content update output is an existing file: '{output}'.");
        if (!Directory.Exists(staging))
            throw new DirectoryNotFoundException($"Content publication staging was not found: '{staging}'.");
        Directory.CreateDirectory(output);

        var sourcePackages = Directory.EnumerateDirectories(staging)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        if (sourcePackages.Length != 1)
            throw new InvalidDataException(
                $"A Player content publication must contain exactly one package, found {sourcePackages.Length}.");

        var sourcePackage = sourcePackages[0];
        cancellationToken.ThrowIfCancellationRequested();
        var packageName = Path.GetFileName(sourcePackage);
        var source = ReadPublicationPointer(sourcePackage, packageName, "staged");
        ValidatePublicationCatalog(source, packageName, cancellationToken);
        using var publishLock = PlayerContentPublishLock.Acquire(output, packageName, cancellationToken);
        var destinationPackage = Path.Combine(output, packageName);
        Directory.CreateDirectory(destinationPackage);
        var current = TryReadPublicationPointer(destinationPackage, packageName, "published");

        var destinationVersion = Path.Combine(destinationPackage, source.Version.Version);
        if (!Directory.Exists(destinationVersion))
        {
            try { Directory.Move(source.VersionDirectory, destinationVersion); }
            catch (IOException) when (Directory.Exists(destinationVersion))
            {
                if (!DirectoriesMatch(source.VersionDirectory, destinationVersion, cancellationToken))
                    throw new InvalidDataException(
                        $"Published content version '{packageName}/{source.Version.Version}' is immutable and differs from " +
                        "the existing remote release.");
            }
        }
        else if (!DirectoriesMatch(source.VersionDirectory, destinationVersion, cancellationToken))
            throw new InvalidDataException(
                $"Published content version '{packageName}/{source.Version.Version}' is immutable and differs from " +
                "the existing remote release.");

        var shouldPromote = current is null ||
                            PlayerBuildSettingsStore.CompareHotResourceVersions(
                                source.Version.Version, current.Version.Version) > 0;
        var selectedVersion = shouldPromote ? source.Version : current!.Version;
        var selectedPointerBytes = CreateLatestPointerBytes(selectedVersion);
        if (shouldPromote || current?.LatestBytes is null ||
            !current.LatestBytes.AsSpan().SequenceEqual(selectedPointerBytes))
            WriteLatestPointer(destinationPackage, selectedPointerBytes, cancellationToken);
        return (selectedVersion.Version, shouldPromote);
    }

    private static PublicationPointer? TryReadPublicationPointer(
        string packageDirectory,
        string packageName,
        string description)
    {
        var latest = Path.Combine(packageDirectory, "latest.json");
        return File.Exists(latest)
            ? ReadPublicationPointer(packageDirectory, packageName, description)
            : null;
    }

    private static PublicationPointer ReadPublicationPointer(
        string packageDirectory,
        string packageName,
        string description)
    {
        var latestPath = Path.Combine(packageDirectory, "latest.json");
        if (!File.Exists(latestPath))
            throw new InvalidDataException($"The {description} content package '{packageName}' has no latest.json.");
        var bytes = File.ReadAllBytes(latestPath);
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
        var normalized = PlayerBuildSettingsStore.NormalizeHotResourceVersion(pointer.Version);
        if (!normalized.Equals(pointer.Version, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"The {description} Hot Resource version '{pointer.Version}' is not canonical.");
        var target = ReadVersionPointer(packageDirectory, packageName, normalized, description);
        if (legacyVersion is not null && !target.VersionBytes.AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException(
                $"The {description} latest.json does not match '{normalized}/version.json'.");
        return target with { LatestBytes = bytes };
    }

    private static PublicationPointer ReadVersionPointer(
        string packageDirectory,
        string packageName,
        string targetVersion,
        string description)
    {
        var versionDirectory = Path.Combine(packageDirectory, targetVersion);
        if (!Directory.Exists(versionDirectory))
            throw new DirectoryNotFoundException(
                $"The {description} content version directory was not found: " +
                $"'{packageName}/{targetVersion}'.");
        var versionPath = Path.Combine(versionDirectory, "version.json");
        if (!File.Exists(versionPath))
            throw new InvalidDataException(
                $"The {description} content version '{packageName}/{targetVersion}' has no version.json.");

        var bytes = File.ReadAllBytes(versionPath);
        var version = AssetBundleCatalogSerializer.DeserializeVersion(bytes);
        if (!version.PackageName.Equals(packageName, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"The {description} version package '{version.PackageName}' does not match '{packageName}'.");
        var normalized = PlayerBuildSettingsStore.NormalizeHotResourceVersion(version.Version);
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
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var relativeCatalog = AssetBundleValidation.NormalizeRelativePath(
            pointer.Version.CatalogFile, "published catalog file");
        var catalogPath = Path.Combine(
            pointer.VersionDirectory,
            relativeCatalog.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(catalogPath))
            throw new InvalidDataException(
                $"The selected content version '{packageName}/{pointer.Version.Version}' has no catalog " +
                $"at '{relativeCatalog}'.");

        var catalogInfo = new FileInfo(catalogPath);
        if (catalogInfo.Length != pointer.Version.CatalogSize)
            throw new InvalidDataException(
                $"The selected content catalog size does not match " +
                $"'{pointer.Version.Version}/version.json'.");
        var catalogBytes = File.ReadAllBytes(catalogPath);
        cancellationToken.ThrowIfCancellationRequested();
        var catalogHash = AssetBundleCatalogSerializer.ComputeSha256(catalogBytes);
        if (!catalogHash.Equals(pointer.Version.CatalogSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"The selected content catalog SHA256 does not match " +
                $"'{pointer.Version.Version}/version.json'.");

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
        CancellationToken cancellationToken)
    {
        var latestPath = Path.Combine(packageDirectory, "latest.json");
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var temporary = Path.Combine(packageDirectory, $".latest.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(
                           temporary,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(latestPath))
                {
                    try { File.Replace(temporary, latestPath, null, ignoreMetadataErrors: true); }
                    catch (PlatformNotSupportedException)
                    {
                        File.Move(temporary, latestPath, overwrite: true);
                    }
                }
                else File.Move(temporary, latestPath);
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
                TryDeleteFile(temporary);
            }
        }
    }

    private static bool DirectoriesMatch(
        string left,
        string right,
        CancellationToken cancellationToken)
    {
        var leftFiles = Directory.EnumerateFiles(left, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(left, path), StringComparer.OrdinalIgnoreCase);
        var rightFiles = Directory.EnumerateFiles(right, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(right, path), StringComparer.OrdinalIgnoreCase);
        if (leftFiles.Count != rightFiles.Count || leftFiles.Keys.Any(path => !rightFiles.ContainsKey(path)))
            return false;
        foreach (var (relative, leftPath) in leftFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rightPath = rightFiles[relative];
            if (new FileInfo(leftPath).Length != new FileInfo(rightPath).Length) return false;
            using var leftStream = File.OpenRead(leftPath);
            using var rightStream = File.OpenRead(rightPath);
            if (!System.Security.Cryptography.SHA256.HashData(leftStream)
                    .SequenceEqual(System.Security.Cryptography.SHA256.HashData(rightStream))) return false;
        }
        return true;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
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

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
