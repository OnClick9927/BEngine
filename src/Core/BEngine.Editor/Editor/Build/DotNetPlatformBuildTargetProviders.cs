using System.Diagnostics;
using BEngine.Build;

namespace BEngine.Editor;

public sealed class DotNetAndroidBuildTargetProvider : DotNetPlatformBuildTargetProvider
{
    public override string ProviderId => "bengine.dotnet-android";
    protected override BuildTargetPlatform Platform => BuildTargetPlatform.Android;

    protected override IEnumerable<PlayerBuildPrerequisite> GetPlatformPrerequisites(
        BuildTargetDescriptor target,
        DotNetBuildEnvironment environment)
    {
        yield return Workload(environment, "android-workload", ["android", "maui-android", "maui"],
            "The .NET Android workload is installed.",
            "The .NET Android workload is not installed.",
            "Run 'dotnet workload install android'.");
        yield return new PlayerBuildPrerequisite(
            "android-sdk",
            environment.AndroidSdkDirectory is not null,
            environment.AndroidSdkDirectory is null
                ? "The Android SDK was not found."
                : $"Android SDK: {environment.AndroidSdkDirectory}",
            "Install Android command-line tools and set ANDROID_SDK_ROOT.");
        yield return new PlayerBuildPrerequisite(
            "java-sdk",
            environment.JavaHome is not null,
            environment.JavaHome is null ? "A Java SDK was not found." : $"Java SDK: {environment.JavaHome}",
            "Install the JDK required by the selected .NET Android workload and set JAVA_HOME.");
        var hybridRuntime = PlatformPlayerBuildCapabilities.TryGetRenderer(target.TargetId, out var host) &&
                            host.SupportsHybridAotInterpreter;
        yield return new PlayerBuildPrerequisite(
            "android-hybrid-aot-interpreter",
            hybridRuntime,
            hybridRuntime
                ? $"Host '{host!.ProviderId}' declares a Core AOT plus downloaded-IL runtime."
                : "The stock .NET Android interpreter disables normal JIT and cannot provide the required " +
                  "Core AOT / downloaded-assembly interpreter boundary.",
            "Register a custom Android Player Host capability with SupportsHybridAotInterpreter=true " +
            "(for example, a verified HybridCLR-style runtime), or replace the Android build provider.");
    }

    protected override void ConfigurePublish(ProcessStartInfo start, PlayerBuildContext context)
    {
        RequireSelfContained(context);
        start.ArgumentList.Add("-p:AotAssemblies=true");
        start.ArgumentList.Add($"-p:ApplicationId={context.Request.AndroidApplicationIdentifier}");
        start.ArgumentList.Add(
            $"-p:SupportedOSPlatformVersion={context.Request.AndroidMinimumApiLevel}.0");
        start.ArgumentList.Add(context.Request.AndroidBuildAppBundle
            ? "-p:AndroidPackageFormat=aab"
            : "-p:AndroidPackageFormat=apk");
    }

    protected override void ValidateOutput(PlayerBuildContext context)
    {
        if (!Directory.EnumerateFiles(context.StagingDirectory, "*.aab", SearchOption.AllDirectories).Any() &&
            !Directory.EnumerateFiles(context.StagingDirectory, "*.apk", SearchOption.AllDirectories).Any())
            throw new InvalidDataException("The Android Player Host did not produce an APK or AAB package.");
    }
}

public sealed class DotNetIosBuildTargetProvider : DotNetPlatformBuildTargetProvider
{
    public override string ProviderId => "bengine.dotnet-ios";
    protected override BuildTargetPlatform Platform => BuildTargetPlatform.IOS;

    protected override IEnumerable<PlayerBuildPrerequisite> GetPlatformPrerequisites(
        BuildTargetDescriptor target,
        DotNetBuildEnvironment environment)
    {
        yield return new PlayerBuildPrerequisite(
            "macos-host",
            OperatingSystem.IsMacOS(),
            OperatingSystem.IsMacOS()
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
        var bridge = PlatformPlayerBuildCapabilities.TryGetRenderer(target.TargetId, out var host)
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

    protected override void ConfigurePublish(ProcessStartInfo start, PlayerBuildContext context)
    {
        RequireSelfContained(context);
        _ = PlatformPlayerBuildCapabilities.TryGetRenderer(context.Target.TargetId, out var host);
        var bridge = host?.InterpreterBridgeAssemblyName ?? "BEngine.Player.iOSHost";
        start.ArgumentList.Add($"-p:ApplicationId={context.Request.IosBundleIdentifier}");
        start.ArgumentList.Add(
            $"-p:SupportedOSPlatformVersion={context.Request.IosMinimumVersion}");
        start.ArgumentList.Add($"-p:MtouchInterpreter={bridge}");
    }

    protected override void ValidateOutput(PlayerBuildContext context)
    {
        if (!Directory.EnumerateDirectories(context.StagingDirectory, "*.app", SearchOption.AllDirectories).Any() &&
            !Directory.EnumerateFiles(context.StagingDirectory, "*.ipa", SearchOption.AllDirectories).Any())
            throw new InvalidDataException("The iOS Player Host did not produce an app bundle or IPA package.");
    }
}

public sealed class DotNetWebAssemblyBuildTargetProvider : DotNetPlatformBuildTargetProvider
{
    public override string ProviderId => "bengine.dotnet-webassembly";
    protected override BuildTargetPlatform Platform => BuildTargetPlatform.Web;

    protected override IEnumerable<PlayerBuildPrerequisite> GetPlatformPrerequisites(
        BuildTargetDescriptor target,
        DotNetBuildEnvironment environment)
    {
        yield return Workload(environment, "wasm-workload",
            ["wasm-tools", "wasm-experimental"],
            "The .NET WebAssembly workload is installed; Release builds AOT the Host/Core and can take substantially longer.",
            "The .NET WebAssembly workload is not installed.",
            "Run 'dotnet workload install wasm-tools'.");
    }

    protected override void ConfigurePublish(ProcessStartInfo start, PlayerBuildContext context)
    {
        RequireSelfContained(context);
        start.ArgumentList.Add("-p:RunAOTCompilation=true");
        start.ArgumentList.Add("-p:WasmBuildNative=true");
    }

    protected override void ValidateOutput(PlayerBuildContext context)
    {
        if (!Directory.EnumerateFiles(context.StagingDirectory, "dotnet.js", SearchOption.AllDirectories).Any() ||
            !Directory.EnumerateFiles(context.StagingDirectory, "index.html", SearchOption.AllDirectories).Any())
            throw new InvalidDataException(
                "The WebAssembly Player Host did not produce index.html and the .NET browser runtime.");
    }
}

public abstract class DotNetPlatformBuildTargetProvider :
    IPlayerBuildTargetProvider,
    IPlayerBuildPrerequisiteProvider
{
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
        var environment = DotNetBuildEnvironmentProbe.Inspect();
        var registeredRenderer = PlatformPlayerBuildCapabilities.TryGetRenderer(
            target.TargetId, out var renderer);
        var hostFound = registeredRenderer
            ? File.Exists(renderer!.HostProjectPath)
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
            $"then register it through {nameof(PlatformPlayerBuildCapabilities)}.{nameof(PlatformPlayerBuildCapabilities.RegisterRenderer)}."));
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
        _ = PlatformPlayerBuildCapabilities.TryGetRenderer(context.Target.TargetId, out var renderer);
        var hostProject = DotNetBuildHostLocator.Resolve(
            context.Target,
            context.Request.PlayerHostProjectPath ?? renderer?.HostProjectPath);
        var workspace = BEngine.ProjectSystem.ProjectWorkspace.Open(context.Request.ProjectPath);
        var layout = PlayerBuildLayout.Create(workspace, context.StagingDirectory);
        context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.CompilingPlayer,
            $"Publishing {Platform} Player for {context.Target.RuntimeIdentifier}", 0.65f));
        await DotNetPublishRunner.RunAsync(hostProject, context, start =>
        {
            start.ArgumentList.Add($"-p:BEngineContentRoot={context.StagingDirectory}");
            start.ArgumentList.Add($"-p:BEngineDataDirectoryName={layout.DataDirectoryName}");
            ConfigurePublish(start, context);
        }, cancellationToken).ConfigureAwait(false);
        context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.Publishing,
            $"Finalizing {Platform} package", 0.9f));
        ValidateOutput(context);
    }

    protected abstract IEnumerable<PlayerBuildPrerequisite> GetPlatformPrerequisites(
        BuildTargetDescriptor target,
        DotNetBuildEnvironment environment);

    protected abstract void ConfigurePublish(ProcessStartInfo start, PlayerBuildContext context);
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
