using BEngine.Build;
using BEngine.Content;
using BEngine.Platform;
using BEngine.ProjectSystem;

namespace BEngine.Editor;

public class DotNetDesktopBuildTargetProvider :
    IPlayerBuildTargetProvider,
    IPlayerBuildPrerequisiteProvider
{
    private readonly ProcessUtility _processRunner;
    private readonly ApplicationPathUtility _applicationPaths;
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;

    public DotNetDesktopBuildTargetProvider() : this(
        ProcessUtility.Current,
        ApplicationPathUtility.Current,
        FileUtility.Current,
        PathUtility.Current)
    {
    }

    public DotNetDesktopBuildTargetProvider(
        ProcessUtility processRunner,
        ApplicationPathUtility applicationPaths,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _applicationPaths = applicationPaths ??
                            throw new ArgumentNullException(nameof(applicationPaths));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
    }

    public virtual string ProviderId => "bengine.dotnet-desktop";

    public virtual bool SupportsTarget(BuildTargetDescriptor target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.HasBuiltInPlayerHost;
    }

    public IReadOnlyList<PlayerBuildPrerequisite> GetPrerequisites(BuildTargetDescriptor target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!SupportsTarget(target)) return [];
        var environment = DotNetBuildEnvironmentProbe.Inspect(_processRunner);
        var hostFound = DotNetBuildHostLocator.TryResolve(target, null, out _);
        return
        [
            new PlayerBuildPrerequisite(
                "dotnet-sdk",
                environment.DotNetAvailable,
                environment.DotNetAvailable
                    ? $".NET SDK {environment.SdkVersion} is available."
                    : $"The .NET SDK is unavailable. {environment.Diagnostic}".Trim(),
                "Install the .NET 10 SDK and ensure 'dotnet' is on PATH."),
            new PlayerBuildPrerequisite(
                "desktop-player-host",
                hostFound,
                hostFound
                    ? "The desktop Player Host source or exported BuildHost template is available."
                    : "The desktop Player Host source and exported BuildHost template are both missing.",
                "Re-export BEngine with the BuildHosts directory, or set PlayerHostProjectPath.")
        ];
    }

    public bool CanBuild(BuildTargetDescriptor target, out string reason)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!SupportsTarget(target))
        {
            reason = $"'{target.TargetId}' is not a desktop target.";
            return false;
        }
        var failed = GetPrerequisites(target).Where(item => !item.IsSatisfied).ToArray();
        reason = string.Join(" ", failed.Select(item =>
            $"{item.Message}{(string.IsNullOrWhiteSpace(item.Remediation) ? string.Empty : $" {item.Remediation}")}"));
        return failed.Length == 0;
    }

    public async Task BuildAsync(PlayerBuildContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!CanBuild(context.Target, out var reason)) throw new PlatformNotSupportedException(reason);
        var playerProject = DotNetBuildHostLocator.Resolve(
            context.Target, context.Request.PlayerHostProjectPath);
        context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.CompilingPlayer,
            $"Publishing Player for {context.Target.RuntimeIdentifier}", 0.65f));
        var workspace = ProjectWorkspace.Open(
            context.Request.ProjectPath, _fileSystem, _pathService);
        var layout = PlayerBuildLayout.Create(workspace, context.StagingDirectory, _fileSystem, _pathService);
        layout.CreateDataDirectories();
        var executablePath = layout.GetExecutablePath(context.Target.Platform);
        var executableName = _pathService.GetFileName(executablePath);

        var publishDirectory = _pathService.Combine(context.StagingDirectory, ".bengine-player-publish");
        _fileSystem.CreateDirectory(publishDirectory);
        try
        {
            await DotNetPublishRunner.RunAsync(
                _processRunner, _applicationPaths, _fileSystem, _pathService,
                playerProject, context, start =>
            {
                start.Arguments.Add("-p:UseAppHost=true");
                start.Arguments.Add("-p:PublishSingleFile=true");
                start.Arguments.Add("-p:IncludeNativeLibrariesForSelfExtract=false");
                if (context.Request.SelfContained)
                    start.Arguments.Add("-p:EnableCompressionInSingleFile=true");
                ConfigurePlatformPublish(start, context);
            }, cancellationToken, publishDirectory).ConfigureAwait(false);

            context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.Publishing,
                "Finalizing Player executable", 0.9f));
            var publishedExecutable = ResolvePublishedExecutable(publishDirectory, context.Target.Platform);
            _fileSystem.MoveFile(publishedExecutable, executablePath);
            foreach (var symbols in _fileSystem.EnumerateFiles(
                         publishDirectory, "*.pdb", SearchOption.TopDirectoryOnly).ToArray())
            {
                if (context.Request.IncludeDebugSymbols)
                    _fileSystem.MoveFile(symbols, _pathService.Combine(
                        layout.AssemblyDirectory, _pathService.GetFileName(symbols).ToLowerInvariant()));
                else
                    _fileSystem.DeleteFile(symbols);
            }
            MoveNativeLibraries(publishDirectory, layout.AssemblyDirectory, context.Target.Platform, context.IncludeTextBindings);
            var unexpected = _fileSystem.EnumerateFileSystemEntries(publishDirectory).ToArray();
            if (unexpected.Length != 0)
                throw new InvalidDataException(
                    $"dotnet publish produced unexpected loose Player files: " +
                    string.Join(", ", unexpected.Select(_pathService.GetFileName)));
        }
        finally
        {
            if (_fileSystem.DirectoryExists(publishDirectory))
                _fileSystem.DeleteDirectory(publishDirectory, recursive: true);
        }

        var rootTargetManifest = _pathService.Combine(context.StagingDirectory, BuildTargetManifest.FileName);
        if (_fileSystem.FileExists(rootTargetManifest))
        {
            var hostTarget = BuildTargetManifestSerializer.Deserialize(_fileSystem.ReadAllBytes(rootTargetManifest));
            if (!hostTarget.TargetId.Equals(context.Target.TargetId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "The platform provider emitted a build-target manifest for a different target.");
            _fileSystem.DeleteFile(rootTargetManifest);
        }

        var playerResources = BuiltInResourceArchive.Open(
            layout.PlayerResourceArchivePath, _fileSystem, _pathService);
        var bootstrap = RuntimeMetadataSerializer.Deserialize(playerResources.ReadBytes(
                PlayerPackagedResourceAddresses.RuntimeMetadata)).PlayerBootstrap ??
            throw new InvalidDataException("The packaged runtime metadata has no Player bootstrap section.");
        var packagedTarget = BuildTargetManifestSerializer.Deserialize(playerResources.ReadBytes(
            PlayerPackagedResourceAddresses.BuildTargetManifest));
        if (!bootstrap.ProductName.Equals(workspace.Project.Name, StringComparison.Ordinal) ||
            !bootstrap.Executable.Equals(executableName, StringComparison.Ordinal) ||
            !packagedTarget.TargetId.Equals(context.Target.TargetId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "The platform provider output does not match the Player bootstrap manifest.");
        ValidatePackagedResourceLayout(layout);
    }

    /// <summary>Applies target-specific runtime settings before the standard desktop publish.</summary>
    protected virtual void ConfigurePlatformPublish(ProcessRunRequest process, PlayerBuildContext context)
    {
    }

    private void ValidatePackagedResourceLayout(PlayerBuildLayout layout)
    {
        var expectedDataEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            PlayerPackagedResourceAddresses.AssemblyDirectoryName,
            PlayerPackagedResourceAddresses.ResourcesDirectoryName
        };
        var dataEntries = _fileSystem.EnumerateFileSystemEntries(layout.DataDirectory).ToArray();
        var actualDataEntries = dataEntries
            .Select(_pathService.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!expectedDataEntries.SetEquals(actualDataEntries) ||
            dataEntries.Any(path => !_fileSystem.DirectoryExists(path)))
            throw new InvalidDataException(
                "The packaged Player data directory must contain only assembly and resources directories.");

        var expectedResourceEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            PlayerPackagedResourceAddresses.PlayerArchiveFileName,
            BuiltInResourceArchive.FileName
        };
        var resourceEntries = _fileSystem.EnumerateFileSystemEntries(layout.ResourcesDirectory).ToArray();
        var actualResourceEntries = resourceEntries
            .Select(_pathService.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!expectedResourceEntries.SetEquals(actualResourceEntries) ||
            resourceEntries.Any(path => !_fileSystem.FileExists(path)))
            throw new InvalidDataException(
                "The packaged Player resources directory must contain only player.bresources and aot.bresources.");
    }

    private string ResolvePublishedExecutable(string publishDirectory, BuildTargetPlatform platform)
    {
        var candidates = _fileSystem.EnumerateFiles(publishDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => platform == BuildTargetPlatform.Windows
                ? _pathService.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                : string.IsNullOrEmpty(_pathService.GetExtension(path)))
            .OrderByDescending(_fileSystem.GetFileLength)
            .ToArray();
        if (candidates.Length == 0)
            throw new InvalidDataException(
                $"dotnet publish did not produce a Player executable in '{publishDirectory}'.");
        return candidates[0];
    }

    private void MoveNativeLibraries(
        string publishDirectory,
        string assemblyDirectory,
        BuildTargetPlatform platform,
        bool includeTextBindings)
    {
        var libraries = _fileSystem.EnumerateFiles(publishDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => IsNativeLibrary(path, platform) &&
                          (includeTextBindings || !IsTextBindingLibrary(_pathService.GetFileName(path))))
            .ToArray();
        if (libraries.Length == 0)
            throw new InvalidDataException(
                $"dotnet publish did not produce a native window library for {platform}.");
        foreach (var library in libraries)
            _fileSystem.MoveFile(library,
                _pathService.Combine(assemblyDirectory, _pathService.GetFileName(library).ToLowerInvariant()), overwrite: true);
    }

    private static bool IsTextBindingLibrary(string name) =>
        name.StartsWith("libskiasharp", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("libharfbuzzsharp", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("skiasharp", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("harfbuzzsharp", StringComparison.OrdinalIgnoreCase);

    private bool IsNativeLibrary(string path, BuildTargetPlatform platform)
    {
        var name = _pathService.GetFileName(path);
        return platform switch
        {
            BuildTargetPlatform.Windows => name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase),
            BuildTargetPlatform.Linux => name.Contains(".so", StringComparison.OrdinalIgnoreCase),
            BuildTargetPlatform.MacOS => name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

}
