using BEngine.Build;
using BEngine.Platform;

namespace BEngine.Editor;

public sealed class DotNetAndroidBuildTargetProvider : DotNetPlatformBuildTargetProvider
{
    public DotNetAndroidBuildTargetProvider()
    {
    }

    public DotNetAndroidBuildTargetProvider(
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
        var hybridRuntime = BuildCapabilities.TryGetRenderer(target.TargetId, out var host) &&
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

    protected override void ConfigurePublish(ProcessRunRequest process, PlayerBuildContext context)
    {
        RequireSelfContained(context);
        process.Arguments.Add("-p:BEnginePlayerPlatform=Android");
        process.Arguments.Add("-p:RunAOTCompilation=true");
        process.Arguments.Add("-p:PublishTrimmed=true");
        process.Arguments.Add("-p:ILLinkTreatWarningsAsErrors=false");
        // Keep Android's native build profile explicit and independent from
        // desktop publish settings.  These properties avoid fast-deployment
        // payloads and keep the AOT/linker path stable
        // for packaged players.
        process.Arguments.Add("-p:AndroidUseSharedRuntime=false");
        process.Arguments.Add("-p:AndroidFastDeployment=false");
        // The default framework profile contains almost none of an engine's
        // rendering, codec or DSP code. Compile the static host closure rather
        // than deferring those cold methods to the first effect/audio frame.
        // Normal Mono AOT keeps downloaded managed assemblies loadable.
        process.Arguments.Add("-p:AndroidEnableProfiledAot=false");
        process.Arguments.Add("-p:AndroidEnableSGenConcurrent=true");
        process.Arguments.Add("-p:AndroidDexTool=d8");
        process.Arguments.Add("-p:AndroidUseAapt2=true");
        process.Arguments.Add($"-p:ApplicationId={context.Request.AndroidApplicationIdentifier}");
        process.Arguments.Add(
            $"-p:SupportedOSPlatformVersion={context.Request.AndroidMinimumApiLevel}.0");
        process.Arguments.Add(context.Request.AndroidBuildAppBundle
            ? "-p:AndroidPackageFormat=aab"
            : "-p:AndroidPackageFormat=apk");
    }

    protected override void ValidateOutput(PlayerBuildContext context)
    {
        NormalizePackageArtifactNames(context.StagingDirectory);
        if (!FileSystem.EnumerateFiles(
                context.StagingDirectory, "*.aab", SearchOption.AllDirectories).Any() &&
            !FileSystem.EnumerateFiles(
                context.StagingDirectory, "*.apk", SearchOption.AllDirectories).Any())
            throw new InvalidDataException("The Android Player Host did not produce an APK or AAB package.");
    }

    private void NormalizePackageArtifactNames(string stagingDirectory)
    {
        var artifacts = FileSystem.EnumerateFiles(stagingDirectory, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var name = PathService.GetFileName(path);
                return name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) ||
                       name.EndsWith(".aab", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains(".apk.", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains(".aab.", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();
        foreach (var source in artifacts)
        {
            var name = PathService.GetFileName(source);
            var normalizedName = name.ToLowerInvariant();
            if (name.Equals(normalizedName, StringComparison.Ordinal)) continue;
            var directory = PathService.GetDirectoryName(source) ??
                            throw new InvalidDataException($"Android package artifact has no directory: '{source}'.");
            var destination = PathService.Combine(directory, normalizedName);
            var temporary = PathService.Combine(directory,
                $".bengine-rename-{Guid.NewGuid():N}.tmp");
            FileSystem.MoveFile(source, temporary);
            try { FileSystem.MoveFile(temporary, destination); }
            catch
            {
                if (!FileSystem.FileExists(source) && FileSystem.FileExists(temporary))
                    FileSystem.MoveFile(temporary, source);
                throw;
            }
        }
    }
}
