using BEngine.Build;
using BEngine.Platform;

namespace BEngine.Editor;

public sealed class DotNetIosBuildTargetProvider : DotNetPlatformBuildTargetProvider
{
    public DotNetIosBuildTargetProvider()
    {
    }

    public DotNetIosBuildTargetProvider(
        ProcessUtility processRunner,
        ApplicationPathUtility applicationPaths,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        PlatformPlayerBuildCapabilities buildCapabilities) : base(
        processRunner, applicationPaths, fileSystem, pathService, platformService,
        buildCapabilities)
    {
    }

    public override string ProviderId => "bengine.dotnet-ios";
    protected override BuildTargetPlatform Platform => BuildTargetPlatform.IOS;

    protected override IEnumerable<PlayerBuildPrerequisite> GetPlatformPrerequisites(
        BuildTargetDescriptor target,
        DotNetBuildEnvironment environment)
    {
        yield return new PlayerBuildPrerequisite(
            "macos-host",
            PlatformService.IsMacOS,
            PlatformService.IsMacOS
                ? "The build is running on macOS."
                : "A device iOS Player can only be compiled and signed on macOS.",
            "Run this build on a macOS host with Xcode installed.");
        yield return Workload(environment, "ios-workload", ["ios", "maui-ios", "maui"],
            "The .NET iOS workload is installed.",
            "The .NET iOS workload is not installed.",
            "Run 'dotnet workload install ios' on the macOS build host.");
        yield return new PlayerBuildPrerequisite(
            "xcode",
            environment.XcodeAvailable,
            environment.XcodeAvailable ? "Xcode command-line tools are available." : "Xcode was not found.",
            "Install Xcode and select it with 'sudo xcode-select -s /Applications/Xcode.app'.");
        var bridge = BuildCapabilities.TryGetRenderer(target.TargetId, out var host)
            ? host.InterpreterBridgeAssemblyName
            : "BEngine.Player.iOSHost";
        yield return new PlayerBuildPrerequisite(
            "ios-interpreter-bridge",
            !string.IsNullOrWhiteSpace(bridge),
            string.IsNullOrWhiteSpace(bridge)
                ? "The iOS Host did not identify its Mono interpreter bridge assembly."
                : $"Only '{bridge}' and downloaded HotUpdate IL use the Mono interpreter; Core remains AOT.",
            "Set InterpreterBridgeAssemblyName when registering the iOS Player Host capability.");
    }

    protected override void ConfigurePublish(ProcessRunRequest process, PlayerBuildContext context)
    {
        RequireSelfContained(context);
        process.Arguments.Add("-p:BEnginePlayerPlatform=iOS");
        _ = BuildCapabilities.TryGetRenderer(context.Target.TargetId, out var host);
        var bridge = host?.InterpreterBridgeAssemblyName ?? "BEngine.Player.iOSHost";
        process.Arguments.Add($"-p:ApplicationId={context.Request.IosBundleIdentifier}");
        process.Arguments.Add(
            $"-p:SupportedOSPlatformVersion={context.Request.IosMinimumVersion}");
        AddSigningArguments(process, context.Request);
        process.Arguments.Add($"-p:MtouchInterpreter={bridge}");
    }

    internal static void AddSigningArguments(
        ProcessRunRequest process,
        PlayerBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(request);
        if (!string.IsNullOrEmpty(request.IosCodesignKey))
            process.Arguments.Add($"-p:CodesignKey={request.IosCodesignKey}");
        if (!string.IsNullOrEmpty(request.IosProvisioningProfile))
            process.Arguments.Add($"-p:CodesignProvision={request.IosProvisioningProfile}");
    }

    protected override void ValidateOutput(PlayerBuildContext context)
    {
        if (!FileSystem.EnumerateDirectories(
                context.StagingDirectory, "*.app", SearchOption.AllDirectories).Any() &&
            !FileSystem.EnumerateFiles(
                context.StagingDirectory, "*.ipa", SearchOption.AllDirectories).Any())
            throw new InvalidDataException("The iOS Player Host did not produce an app bundle or IPA package.");
    }
}
