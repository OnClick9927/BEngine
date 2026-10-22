using BEngine.Build;
using BEngine.Content;
using BEngine.Platform;
using BEngine.ProjectSystem;

namespace BEngine.Player;

public sealed class SystemPlayerStartupResolver : IPlayerStartupResolver
{
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _paths;
    private readonly PlatformUtility _platform;

    public SystemPlayerStartupResolver(
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _paths = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _platform = platformService ?? throw new ArgumentNullException(nameof(platformService));
    }

    public string BaseDirectory => _platform.BaseDirectory;
    public string? PersistentDataPath =>
        _platform.GetEnvironmentVariable("BENGINE_PLAYER_PERSISTENT_DATA_PATH");

    public PlayerStartupDescriptor Resolve(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count != 0 &&
            arguments[0].Equals("--validate", StringComparison.OrdinalIgnoreCase))
            return arguments.Count > 1
                ? new PlayerStartupDescriptor(_paths.GetFullPath(arguments[1]), BaseDirectory, null)
                : ResolvePackagedStartup(BaseDirectory);
        return arguments.Count == 0
            ? ResolvePackagedStartup(BaseDirectory)
            : new PlayerStartupDescriptor(_paths.GetFullPath(arguments[0]), BaseDirectory, null);
    }

    public string ResolveDefaultProjectPath(string? playerDirectory = null) =>
        ResolvePackagedStartup(playerDirectory ?? BaseDirectory).ProjectPath;

    private PlayerStartupDescriptor ResolvePackagedStartup(string playerDirectory)
    {
        var root = _paths.TrimEndingDirectorySeparator(_paths.GetFullPath(playerDirectory));
        var legacyBootstrapPath = _paths.Combine(root, PlayerBootstrapManifest.FileName);
        if (_fileSystem.FileExists(legacyBootstrapPath))
            return CreatePackagedStartup(
                root, PlayerBootstrapManifestSerializer.Load(root, _fileSystem, _paths));

        var executableName = _platform.ProcessPath is { Length: > 0 } processPath &&
                             _paths.GetDirectoryName(_paths.GetFullPath(processPath))
                                 ?.Equals(root, StringComparison.OrdinalIgnoreCase) == true
            ? _paths.GetFileNameWithoutExtension(processPath)
            : null;
        if (!string.IsNullOrWhiteSpace(executableName))
        {
            var directDataDirectory = _paths.Combine(
                root, executableName + PlayerPackagedResourceAddresses.DataDirectorySuffix);
            if (_fileSystem.DirectoryExists(directDataDirectory) &&
                (_fileSystem.GetAttributes(directDataDirectory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The Player data directory cannot be a linked directory.");
            var directArchive = GetPlayerResourceArchivePath(directDataDirectory);
            if (_fileSystem.FileExists(directArchive))
                return LoadArchivedPackagedStartup(root, directDataDirectory, directArchive);
            var directMetadata = _paths.Combine(
                directDataDirectory, "res", RuntimeMetadataSerializer.FileName);
            if (_fileSystem.FileExists(directMetadata)) return LoadLoosePackagedStartup(root, directMetadata);
        }

        if (!_fileSystem.DirectoryExists(root))
            return new PlayerStartupDescriptor(_paths.Combine(root, "Game"), root, null);
        var dataDirectories = _fileSystem.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .Where(path => _paths.GetFileName(path).EndsWith(
                PlayerPackagedResourceAddresses.DataDirectorySuffix,
                StringComparison.OrdinalIgnoreCase))
            .Where(path => (_fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            .ToArray();
        var candidates = dataDirectories
            .Select(CreateMetadataSource)
            .Where(static source => source is not null)
            .Cast<PackagedMetadataSource>()
            .ToArray();
        if (candidates.Length == 0)
            return new PlayerStartupDescriptor(_paths.Combine(root, "Game"), root, null);

        var valid = new List<PlayerStartupDescriptor>(candidates.Length);
        var failures = new List<Exception>();
        foreach (var candidate in candidates)
        {
            try
            {
                valid.Add(candidate.IsArchive
                    ? LoadArchivedPackagedStartup(root, candidate.DataDirectory, candidate.Path)
                    : LoadLoosePackagedStartup(root, candidate.Path));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                               InvalidDataException)
            {
                failures.Add(exception);
            }
        }
        if (valid.Count == 1) return valid[0];
        if (valid.Count > 1)
            throw new InvalidDataException(
                $"The Player directory contains multiple runtime data folders: '{root}'.");
        throw new InvalidDataException(
            $"No valid Player runtime metadata was found below '{root}'.",
            failures.Count == 1 ? failures[0] : new AggregateException(failures));
    }

    private PackagedMetadataSource? CreateMetadataSource(string dataDirectory)
    {
        var archive = GetPlayerResourceArchivePath(dataDirectory);
        if (_fileSystem.FileExists(archive))
            return new PackagedMetadataSource(dataDirectory, archive, IsArchive: true);
        var metadata = _paths.Combine(dataDirectory, "res", RuntimeMetadataSerializer.FileName);
        return _fileSystem.FileExists(metadata)
            ? new PackagedMetadataSource(dataDirectory, metadata, IsArchive: false)
            : null;
    }

    private string GetPlayerResourceArchivePath(string dataDirectory) => _paths.Combine(
        dataDirectory,
        PlayerPackagedResourceAddresses.ResourcesDirectoryName,
        PlayerPackagedResourceAddresses.PlayerArchiveFileName);

    private PlayerStartupDescriptor LoadArchivedPackagedStartup(
        string root,
        string dataDirectory,
        string archivePath)
    {
        EnsurePhysicalDirectory(dataDirectory, "data");
        var resourcesDirectory = _paths.GetDirectoryName(_paths.GetFullPath(archivePath)) ??
                                 throw new InvalidDataException(
                                     "The Player resource archive path is invalid.");
        EnsurePhysicalDirectory(resourcesDirectory, "resource");
        EnsurePhysicalFile(archivePath, "resource archive");
        var archive = BuiltInResourceArchive.Open(archivePath, _fileSystem, _paths);
        var metadata = RuntimeMetadataSerializer.Deserialize(
            archive.ReadBytes(PlayerPackagedResourceAddresses.RuntimeMetadata));
        var manifest = metadata.PlayerBootstrap ??
                       throw new InvalidDataException(
                           $"Player resource archive has no bootstrap metadata: '{archivePath}'.");
        var expectedDataDirectory = _paths.TrimEndingDirectorySeparator(_paths.GetFullPath(dataDirectory));
        var actualDataDirectory = _paths.TrimEndingDirectorySeparator(manifest.ResolveDataDirectory(root));
        var actualResourceDirectory = _paths.TrimEndingDirectorySeparator(
            manifest.ResolveResourceDirectory(root));
        var actualArchive = _paths.GetFullPath(manifest.ResolvePlayerResourceArchive(root));
        if (!actualDataDirectory.Equals(expectedDataDirectory, StringComparison.OrdinalIgnoreCase) ||
            !actualResourceDirectory.Equals(expectedDataDirectory, StringComparison.OrdinalIgnoreCase) ||
            !actualArchive.Equals(_paths.GetFullPath(archivePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Player resource archive location does not match its bootstrap data directories.");
        EnsurePhysicalDirectory(manifest.ResolveAssemblyDirectory(root), "assembly");
        return CreatePackagedStartup(root, manifest);
    }

    private PlayerStartupDescriptor LoadLoosePackagedStartup(string root, string metadataPath)
    {
        var metadata = RuntimeMetadataSerializer.Load(metadataPath, _fileSystem, _paths);
        var manifest = metadata.PlayerBootstrap ??
                       throw new InvalidDataException(
                           $"Player runtime metadata has no bootstrap section: '{metadataPath}'.");
        var expectedResourceDirectory = _paths.TrimEndingDirectorySeparator(
            _paths.GetDirectoryName(_paths.GetFullPath(metadataPath))!);
        var expectedDataDirectory = _paths.TrimEndingDirectorySeparator(
            _paths.GetDirectoryName(expectedResourceDirectory) ?? string.Empty);
        var actualDataDirectory = _paths.TrimEndingDirectorySeparator(
            manifest.ResolveDataDirectory(root));
        var actualResourceDirectory = _paths.TrimEndingDirectorySeparator(
            manifest.ResolveResourceDirectory(root));
        if (!actualDataDirectory.Equals(expectedDataDirectory, StringComparison.OrdinalIgnoreCase) ||
            !actualResourceDirectory.Equals(expectedResourceDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Player runtime metadata location does not match its bootstrap data directories.");
        EnsurePhysicalDirectory(actualDataDirectory, "data");
        EnsurePhysicalDirectory(actualResourceDirectory, "resource");
        EnsurePhysicalDirectory(manifest.ResolveAssemblyDirectory(root), "assembly");
        return CreatePackagedStartup(root, manifest);
    }

    private void EnsurePhysicalDirectory(string path, string description)
    {
        if (!_fileSystem.DirectoryExists(path))
            throw new DirectoryNotFoundException(
                $"The packaged Player {description} directory is missing: '{path}'.");
        if ((_fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                $"The packaged Player {description} directory cannot be a linked directory: '{path}'.");
    }

    private void EnsurePhysicalFile(string path, string description)
    {
        if (!_fileSystem.FileExists(path))
            throw new FileNotFoundException($"The packaged Player {description} is missing.", path);
        if ((_fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                $"The packaged Player {description} cannot be a linked file: '{path}'.");
    }

    private PlayerStartupDescriptor CreatePackagedStartup(
        string root,
        PlayerBootstrapManifest manifest)
    {
        if (manifest.Version >= 4)
        {
            var archivePath = manifest.ResolvePlayerResourceArchive(root);
            EnsurePhysicalFile(archivePath, "resource archive");
            var archive = BuiltInResourceArchive.Open(archivePath, _fileSystem, _paths);
            BuildTargetManifestSerializer.SetCurrent(
                archive.ReadBytes(manifest.BuildTargetResource));
        }
        else
        {
            var targetManifest = manifest.ResolveBuildTargetManifest(root);
            BuildTargetManifestSerializer.SetCurrentPath(targetManifest, _fileSystem, _paths);
        }
        return new PlayerStartupDescriptor(
            manifest.ResolveResourceDirectory(root),
            manifest.ResolveAssemblyDirectory(root),
            manifest);
    }

    private sealed record PackagedMetadataSource(
        string DataDirectory,
        string Path,
        bool IsArchive);
}
