using BEngine.Build;
using BEngine.Platform;

namespace BEngine.Editor;

public abstract class DotNetPlatformBuildTargetProvider :
    IPlayerBuildTargetProvider,
    IPlayerBuildPrerequisiteProvider
{
    private readonly ProcessUtility _processRunner;
    private readonly ApplicationPathUtility _applicationPaths;
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;
    private readonly PlatformPlayerBuildCapabilities _buildCapabilities;

    protected DotNetPlatformBuildTargetProvider() : this(
        ProcessUtility.Current,
        ApplicationPathUtility.Current,
        FileUtility.Current,
        PathUtility.Current,
        PlatformUtility.Current,
        EditorServiceAccess.GetRequiredService<PlatformPlayerBuildCapabilities>())
    {
    }

    protected DotNetPlatformBuildTargetProvider(
        ProcessUtility processRunner,
        ApplicationPathUtility applicationPaths,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        PlatformPlayerBuildCapabilities buildCapabilities)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _applicationPaths = applicationPaths ??
                            throw new ArgumentNullException(nameof(applicationPaths));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        PlatformService = platformService ??
                          throw new ArgumentNullException(nameof(platformService));
        _buildCapabilities = buildCapabilities ??
                             throw new ArgumentNullException(nameof(buildCapabilities));
    }

    protected PlatformUtility PlatformService { get; }
    protected FileUtility FileSystem => _fileSystem;
    protected PathUtility PathService => _pathService;
    protected PlatformPlayerBuildCapabilities BuildCapabilities => _buildCapabilities;

    public abstract string ProviderId { get; }
    protected abstract BuildTargetPlatform Platform { get; }

    public bool SupportsTarget(BuildTargetDescriptor target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.Platform == Platform;
    }

    public IReadOnlyList<PlayerBuildPrerequisite> GetPrerequisites(BuildTargetDescriptor target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!SupportsTarget(target)) return [];
        var environment = DotNetBuildEnvironmentProbe.Inspect(_processRunner);
        var registeredRenderer = _buildCapabilities.TryGetRenderer(
            target.TargetId, out var renderer);
        var hostFound = registeredRenderer
            ? _fileSystem.FileExists(renderer!.HostProjectPath)
            : DotNetBuildHostLocator.TryResolve(target, null, out _);
        var prerequisites = new List<PlayerBuildPrerequisite>
        {
            new(
                "dotnet-sdk",
                environment.DotNetAvailable,
                environment.DotNetAvailable
                    ? $".NET SDK {environment.SdkVersion} is available."
                    : $"The .NET SDK is unavailable. {environment.Diagnostic}".Trim(),
                "Install the .NET 10 SDK and ensure 'dotnet' is on PATH."),
            new(
                "player-host-template",
                hostFound,
                hostFound
                    ? $"The {Platform} Player Host template is available."
                    : $"The {Platform} Player Host template is missing.",
                "Re-export BEngine with the BuildHosts directory, or set PlayerHostProjectPath.")
        };
        prerequisites.AddRange(GetPlatformPrerequisites(target, environment));
        var rendererFound = registeredRenderer;
        prerequisites.Add(new PlayerBuildPrerequisite(
            "platform-renderer",
            rendererFound,
            rendererFound
                ? $"Renderer '{renderer!.ProviderId}' provides {string.Join(", ", renderer.Backends)}."
                : $"No runnable {target.TargetId} renderer is registered; a logic-only shell is not a Player build.",
            $"Install a Player Host renderer for one of: {string.Join(", ", target.GraphicsBackends)}; " +
            $"then resolve {nameof(PlatformPlayerBuildCapabilities)} from Editor DI and call " +
            $"{nameof(PlatformPlayerBuildCapabilities.RegisterRenderer)}."));
        return prerequisites;
    }

    public bool CanBuild(BuildTargetDescriptor target, out string reason)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!SupportsTarget(target))
        {
            reason = $"'{target.TargetId}' is not a {Platform} target.";
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
        _ = _buildCapabilities.TryGetRenderer(context.Target.TargetId, out var renderer);
        var hostProject = DotNetBuildHostLocator.Resolve(
            context.Target,
            context.Request.PlayerHostProjectPath ?? renderer?.HostProjectPath);
        var workspace = BEngine.ProjectSystem.ProjectWorkspace.Open(
            context.Request.ProjectPath, _fileSystem, _pathService);
        var layout = PlayerBuildLayout.Create(
            workspace, context.StagingDirectory, _fileSystem, _pathService);
        context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.CompilingPlayer,
            $"Publishing {Platform} Player for {context.Target.RuntimeIdentifier}", 0.65f));
        await DotNetPublishRunner.RunAsync(
            _processRunner, _applicationPaths, _fileSystem, _pathService,
            hostProject, context, start =>
        {
            start.Arguments.Add($"-p:BEngineContentRoot={context.StagingDirectory}");
            start.Arguments.Add($"-p:BEngineDataDirectoryName={layout.DataDirectoryName}");
            ConfigurePublish(start, context);
        }, cancellationToken).ConfigureAwait(false);
        context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.Publishing,
            $"Finalizing {Platform} package", 0.9f));
        ValidateOutput(context);
    }

    protected abstract IEnumerable<PlayerBuildPrerequisite> GetPlatformPrerequisites(
        BuildTargetDescriptor target,
        DotNetBuildEnvironment environment);

    protected abstract void ConfigurePublish(ProcessRunRequest process, PlayerBuildContext context);
    protected abstract void ValidateOutput(PlayerBuildContext context);

    protected static PlayerBuildPrerequisite Workload(
        DotNetBuildEnvironment environment,
        string id,
        string[] workloadIds,
        string success,
        string failure,
        string remediation) =>
        new(id, environment.HasWorkload(workloadIds),
            environment.HasWorkload(workloadIds) ? success : failure, remediation);

    protected static void RequireSelfContained(PlayerBuildContext context)
    {
        if (!context.Request.SelfContained)
            throw new ArgumentException(
                $"'{context.Target.TargetId}' requires a self-contained platform runtime.",
                nameof(context.Request.SelfContained));
    }
}
