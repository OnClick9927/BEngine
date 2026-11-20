using System.Text.RegularExpressions;
using BEngine.Build;
using BEngine.Platform;

namespace BEngine.Editor;

internal static partial class DotNetBuildHostLocator
{
    internal static bool TryResolve(BuildTargetDescriptor target, string? overridePath, out string projectPath,
        PlatformPlayerBuildCapabilities? capabilities = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            projectPath = PathUtility.Current.GetFullPath(overridePath);
            return FileUtility.Current.FileExists(projectPath);
        }
        return (capabilities ?? EditorServices.GetRequiredService<PlatformPlayerBuildCapabilities>())
            .TryGetHostProject(target.TargetId, out projectPath);
    }
    internal static string Resolve(BuildTargetDescriptor target, string? overridePath,
        PlatformPlayerBuildCapabilities? capabilities = null)
    {
        if (TryResolve(target, overridePath, out var projectPath, capabilities)) return projectPath;
        throw new FileNotFoundException($"No host project was registered for '{target.TargetId}'. Supply an explicit host project or register a platform host capability.");
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex MsBuildPropertyNamePattern();

    internal static void AddAdditionalProperties(ProcessRunRequest process, PlatformBuildRequest request)
    {
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "OutputPath", "PublishDir", "RuntimeIdentifier", "TargetFramework",
            "BEngineContentRoot", "BEngineEngineRoot", "BEngineResolvedRuntimeRoot", "BEngineDesktopGraphics", "BEngineRuntimeShaderCompiler", "PublishAot", "PublishTrimmed",
            "PublishSingleFile", "IncludeNativeLibrariesForSelfExtract", "UseAppHost",
            "RunAOTCompilation", "WasmBuildNative", "AotAssemblies", "UseInterpreter",
            "AndroidUseInterpreter", "MtouchInterpreter", "ArtifactsPath", "UseArtifactsOutput",
            "MonoAndroidAssetsDirIntermediate",
            "BEngineWasmIntermediateRoot",
            "BEngineWebPrecompiledProps", "BEngineWebPrecompiledManifest", "BEngineNativePrecompiledProps",
            "BaseOutputPath", "BaseIntermediateOutputPath", "MSBuildProjectExtensionsPath", "IntermediateLinkDir",
            "BEngineTrimmerRootDescriptor", "BEngineHostAssemblyBootstrap", "BEngineFullTrimming", "BEngineIncludeTextBindings", "BEngineWebFontBackend", "TrimMode", "JsonSerializerIsReflectionEnabledByDefault", "ApplicationId", "SupportedOSPlatformVersion",
            "AndroidPackageFormat", "BEnginePlayerPlatform", "CodesignKey", "CodesignProvision",
            "BEngineUncompressedResourceExtensions", "AndroidStoreUncompressedFileExtensions",
            "BEngineWindowsGarbageCollection", "BEngineWindowsGcHeapCount"
        };
        foreach (var (name, value) in request.AdditionalMsBuildProperties.OrderBy(pair => pair.Key,
                     StringComparer.Ordinal))
        {
            var overridesWindowsGc = request.Options.Get<WindowsBuildOptions>().WindowsGarbageCollection != PlayerGarbageCollectionMode.RuntimeDefault &&
                request.TargetId.StartsWith("windows", StringComparison.OrdinalIgnoreCase) &&
                name.Equals("ServerGarbageCollection", StringComparison.OrdinalIgnoreCase);
            var overridesWebPrecompilation = request.Options.Get<WebBuildOptions>().WebScriptCompilation == WebScriptCompilationMode.AheadOfTime &&
                request.TargetId.StartsWith("web", StringComparison.OrdinalIgnoreCase) &&
                WebPrecompiledScriptBuild.IsProtectedPublishProperty(name);
            if (!MsBuildPropertyNamePattern().IsMatch(name) || reserved.Contains(name) || overridesWindowsGc || overridesWebPrecompilation)
                throw new ArgumentException($"MSBuild property '{name}' is invalid or reserved.",
                    nameof(request.AdditionalMsBuildProperties));
            if (value.Contains('\r') || value.Contains('\n') || value.IndexOf('\0') >= 0)
                throw new ArgumentException($"MSBuild property '{name}' contains a control character.",
                    nameof(request.AdditionalMsBuildProperties));
            process.Arguments.Add($"-p:{name}={value}");
        }
    }
}

internal static class DotNetPublishRunner
{
    internal static async Task RunAsync(
        ProcessUtility processRunner,
        ApplicationPathUtility applicationPaths,
        FileUtility fileSystem,
        PathUtility pathService,
        string projectPath,
        PlatformBuildContext context,
        Action<ProcessRunRequest>? configure = null,
        CancellationToken cancellationToken = default,
        string? outputDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(applicationPaths);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        var buildArtifactsDirectory = pathService.Combine(
            context.Request.ProjectPath, "Temp", "PlayerBuildArtifacts", Guid.NewGuid().ToString("N"));
        fileSystem.CreateDirectory(buildArtifactsDirectory);
        try
        {
        var process = new ProcessRunRequest("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = pathService.GetDirectoryName(projectPath)!
        };
        process.Arguments.Add("publish");
        process.Arguments.Add(projectPath);
        process.Arguments.Add("--configuration");
        process.Arguments.Add(context.Request.Configuration);
        process.Arguments.Add("--runtime");
        process.Arguments.Add(context.Target.RuntimeIdentifier);
        process.Arguments.Add("--self-contained");
        process.Arguments.Add(context.Request.SelfContained ? "true" : "false");
        process.Arguments.Add("--output");
        process.Arguments.Add(outputDirectory ?? context.StagingDirectory);
        process.Arguments.Add("--artifacts-path");
        process.Arguments.Add(buildArtifactsDirectory);
        // ILLink can write long paths that MSBuild's Exists condition rejects on
        // Windows. Keep linked images near the isolated root so the SDK retains
        // them in both the publish list and dependency manifest.
        process.Arguments.Add($"-p:IntermediateLinkDir={pathService.Combine(buildArtifactsDirectory, "linked")}{Path.DirectorySeparatorChar}");
        if (context.Target.Platform == BuildTargetPlatform.Android)
        {
            // AAPT2 on Windows silently skips asset directories past its native
            // path limit. Avoid the extra obj/host/configuration nesting while
            // retaining this build's isolated, automatically cleaned Temp root.
            process.Arguments.Add($"-p:MonoAndroidAssetsDirIntermediate={pathService.Combine(buildArtifactsDirectory, "assets")}{Path.DirectorySeparatorChar}");
        }
        else if (context.Target.Platform == BuildTargetPlatform.Web)
        {
            // .NET Mono's native AOT compiler may return success without writing
            // a method-token path past Windows MAX_PATH. The SDK currently
            // initializes its private output paths from IntermediateOutputPath.
            // The Web Host uses this root only during that SDK initialization,
            // preserving its regular managed obj paths and nested-publish split.
            process.Arguments.Add($"-p:BEngineWasmIntermediateRoot={buildArtifactsDirectory}{Path.DirectorySeparatorChar}");
        }
        process.Arguments.Add("--nologo");
        process.Arguments.Add("--disable-build-servers");
        process.Arguments.Add("-nodeReuse:false");
        process.Arguments.Add("-maxcpucount:1");
        // Publishing a game must not rewrite the shared engine package repository.
        // Multiple target variants otherwise race with editor/package builds over export folders.
        process.Arguments.Add("-p:BEnginePackageExportOwner=false");
        process.Arguments.Add("-p:BEngineEngineExportOwner=false");
        process.Arguments.Add($"-p:DebugSymbols={context.Request.IncludeDebugSymbols.ToString().ToLowerInvariant()}");
        // DebugSymbols=false alone does not suppress an explicitly embedded PDB.
        // Honor the Player's symbol setting in the compiler's debug format too.
        process.Arguments.Add(context.Request.IncludeDebugSymbols ? "-p:DebugType=embedded" : "-p:DebugType=none");
        process.Arguments.Add("-p:PublishAot=false");
        if (context.Target.HasBuiltInPlayerHost)
        {
            process.Arguments.Add($"-p:BEngineDesktopGraphics={DesktopGraphicsVariant(context.GraphicsBackends)}");
            process.Arguments.Add($"-p:BEngineRuntimeShaderCompiler={context.Request.RuntimeShaderCompilation.ToString().ToLowerInvariant()}");
        }
        var fullTrimming = context.Request.ManagedStripping == PlayerManagedStrippingLevel.Full;
        if (fullTrimming && !context.Request.SelfContained)
            throw new InvalidDataException("Full Player stripping requires a self-contained build.");
        var trimManagedCode = context.Request.SelfContained &&
                              context.Request.ManagedStripping != PlayerManagedStrippingLevel.Disabled;
        process.Arguments.Add($"-p:BEngineFullTrimming={fullTrimming.ToString().ToLowerInvariant()}");
        context.Hooks.ConfigurePublish?.Invoke(context, buildArtifactsDirectory, process);
        process.Arguments.Add("-p:NuGetAudit=false");
        process.Arguments.Add("-p:RestoreIgnoreFailedSources=true");
        var engineRoot = context.Request.EngineRuntimeRoot;
        if (engineRoot is not null) process.Arguments.Add($"-p:BEngineEngineRoot={engineRoot}");
        if (context.Target.Platform == BuildTargetPlatform.Web &&
            context.PrecompiledManagedCodeAssemblyPaths.Count > 0)
            process.Arguments.Add($"-p:BEngineWebPrecompiledProps={WebPrecompiledScriptBuild.Write(context, buildArtifactsDirectory)}");
        if (context.Target.Platform is BuildTargetPlatform.Android or BuildTargetPlatform.IOS &&
            context.PrecompiledManagedCodeAssemblyPaths.Count > 0)
            process.Arguments.Add($"-p:BEngineNativePrecompiledProps={NativePrecompiledScriptBuild.Write(context, buildArtifactsDirectory)}");
        configure?.Invoke(process);
        DotNetBuildHostLocator.AddAdditionalProperties(process, context.Request);
        BEngine.Tooling.ToolEnvironment.ApplyToDotNetBuild(process.EnvironmentVariables);

            var result = await processRunner.RunAsync(process, cancellationToken).ConfigureAwait(false);
            context.Hooks.Published?.Invoke(context, buildArtifactsDirectory, result);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"dotnet publish failed for {context.Target.TargetId} ({result.ExitCode}).\n" +
                    result.StandardOutput + result.StandardError);
        }
        finally
        {
            DeleteTemporaryArtifacts(fileSystem, buildArtifactsDirectory);
        }
    }

    private static string DesktopGraphicsVariant(IReadOnlyList<BEngine.Rendering.Rhi.GraphicsBackend> backends) =>
        PlatformGraphicsBuildPolicy.DesktopVariant(backends);

    private static void DeleteTemporaryArtifacts(FileUtility fileSystem, string directory)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (fileSystem.DirectoryExists(directory))
                    fileSystem.DeleteDirectory(directory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) when (attempt < 4) { Thread.Sleep(100); }
        }
    }
}
