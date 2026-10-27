using BEngine.Build;
using BEngine.Platform;
using BEngine.AssetBundles;
using BEngine.Serialization;

namespace BEngine.Editor;

public sealed class DotNetWebAssemblyBuildTargetProvider : DotNetPlatformBuildTargetProvider
{
    public DotNetWebAssemblyBuildTargetProvider()
    {
    }

    public DotNetWebAssemblyBuildTargetProvider(
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

    protected override void ConfigurePublish(ProcessRunRequest process, PlayerBuildContext context)
    {
        RequireSelfContained(context);
        process.Arguments.Add($"-p:BEngineHttpStreamingAssets={UsesHttpStreamingAssets(context).ToString().ToLowerInvariant()}");
        // Make the platform a global restore property so the BEngine.Player project graph and
        // its platform-suffixed artifacts directory agree during publish.
        process.Arguments.Add("-p:BEnginePlayerPlatform=Web");
        process.Arguments.Add("-p:PublishTrimmed=true");
        process.Arguments.Add(context.Request.ManagedStripping == PlayerManagedStrippingLevel.Full
            ? "-p:TrimMode=full" : "-p:TrimMode=partial");
        process.Arguments.Add(context.Request.ManagedStripping == PlayerManagedStrippingLevel.Full
            ? "-p:SuppressTrimAnalysisWarnings=false" : "-p:SuppressTrimAnalysisWarnings=true");
        process.Arguments.Add("-p:RunAOTCompilation=true");
        process.Arguments.Add("-p:WasmBuildNative=true");
        process.Arguments.Add("-p:WasmDedup=true");
        process.Arguments.Add("-p:WasmNativeDebugSymbols=false");
        if (string.Equals(context.Request.Configuration, "Release", StringComparison.OrdinalIgnoreCase))
        {
            // Optimize the AOT bitcode, native runtime and final WebAssembly for speed.
            // Keep the SDK's arithmetic, exception and target-feature settings.
            process.Arguments.Add("-p:WasmBitcodeCompileOptimizationFlag=-O3");
            process.Arguments.Add("-p:EmccCompileOptimizationFlag=-O3");
            process.Arguments.Add("-p:EmccLinkOptimizationFlag=-O3");
        }
    }

    protected override void ValidateOutput(PlayerBuildContext context)
    {
        var webRoot = PathService.Combine(context.StagingDirectory, "wwwroot");
        WebPlayerContentLayout.Apply(context.StagingDirectory, webRoot, FileSystem, PathService,
            UsesHttpStreamingAssets(context));
        if (!FileSystem.FileExists(PathService.Combine(webRoot, "index.html")) ||
            !FileSystem.FileExists(PathService.Combine(webRoot, "main.js")) ||
            !FileSystem.FileExists(PathService.Combine(webRoot, "framework.js")) ||
            !FileSystem.FileExists(PathService.Combine(webRoot, "bengine-content.json")) ||
            !FileSystem.FileExists(PathService.Combine(
                webRoot, "_framework", "dotnet.js")))
            throw new InvalidDataException(
                "The WebAssembly Player Host did not produce a deployable wwwroot with " +
                "index.html, main.js, framework.js, bengine-content.json, and _framework/dotnet.js.");
        WebPlayerRuntimeBundle.Apply(webRoot, FileSystem, PathService);
        WebPlayerDistribution.Apply(webRoot, FileSystem, PathService);
        WebPlayerCacheVersioning.Apply(webRoot, FileSystem, PathService);
    }

    private bool UsesHttpStreamingAssets(PlayerBuildContext context)
    {
        var settingsPath = PathService.Combine(context.Request.ProjectPath, "ProjectSettings", AssetBundleSettingsDocument.FileName);
        var settings = FileSystem.FileExists(settingsPath)
            ? YamlUtility.Load<AssetBundleSettingsDocument>(settingsPath, FileSystem, PathService) : null;
        if (settings is not { Enabled: true, LoadMode: AssetBundleLoadMode.StreamingAssets }) return false;
        var directory = settings.BuiltInDirectory.Replace('\\', '/').TrimEnd('/');
        if (!(directory.Equals("StreamingAssets", StringComparison.OrdinalIgnoreCase) ||
              directory.StartsWith("StreamingAssets/", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Web direct AssetBundles must be placed under StreamingAssets.");
        return true;
    }

}
