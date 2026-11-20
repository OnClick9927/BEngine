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
            : null;
        yield return new PlayerBuildPrerequisite(
            "ios-interpreter-bridge",
            !string.IsNullOrWhiteSpace(bridge),
            string.IsNullOrWhiteSpace(bridge)
                ? "The iOS Host did not identify its Mono interpreter bridge assembly."
                : $"Only '{bridge}' and runtime-loaded IL use the Mono interpreter; Core remains AOT.",
            "Set InterpreterBridgeAssemblyName when registering the iOS Player Host capability.");
    }

    protected override void ConfigurePublish(ProcessRunRequest process, PlatformBuildContext context)
    {
        RequireSelfContained(context);
        process.Arguments.Add("-p:BEnginePlayerPlatform=iOS");
        // Restore and publish must agree on the renderer's dependency closure.
        process.Arguments.Add("-p:BEngineRuntimeShaderCompiler=false");
        _ = BuildCapabilities.TryGetRenderer(context.Target.TargetId, out var host);
        var bridge = host?.InterpreterBridgeAssemblyName ?? throw new InvalidOperationException("The iOS host must register its interpreter bridge.");
        process.Arguments.Add($"-p:ApplicationId={context.Request.Options.Get<IosBuildOptions>().IosBundleIdentifier}");
        process.Arguments.Add(
            $"-p:SupportedOSPlatformVersion={context.Request.Options.Get<IosBuildOptions>().IosMinimumVersion}");
        AddSigningArguments(process, context.Request);
        process.Arguments.Add($"-p:MtouchInterpreter={bridge}");
    }

    internal static void AddSigningArguments(
        ProcessRunRequest process,
        PlatformBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(request);
        if (!string.IsNullOrEmpty(request.Options.Get<IosBuildOptions>().IosCodesignKey))
            process.Arguments.Add($"-p:CodesignKey={request.Options.Get<IosBuildOptions>().IosCodesignKey}");
        if (!string.IsNullOrEmpty(request.Options.Get<IosBuildOptions>().IosProvisioningProfile))
            process.Arguments.Add($"-p:CodesignProvision={request.Options.Get<IosBuildOptions>().IosProvisioningProfile}");
    }

    protected override void ValidateOutput(PlatformBuildContext context)
    {
        if (!FileSystem.EnumerateDirectories(
                context.StagingDirectory, "*.app", SearchOption.AllDirectories).Any() &&
            !FileSystem.EnumerateFiles(
                context.StagingDirectory, "*.ipa", SearchOption.AllDirectories).Any())
            throw new InvalidDataException("The iOS Player Host did not produce an app bundle or IPA package.");
    }
}
