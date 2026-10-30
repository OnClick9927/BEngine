using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Reflection.PortableExecutable;
using BEngine.Build;
using BEngine.Platform;

namespace BEngine.Editor;

public sealed record DotNetBuildEnvironment(
    bool DotNetAvailable,
    string SdkVersion,
    IReadOnlySet<string> InstalledWorkloads,
    string? AndroidSdkDirectory,
    string? JavaHome,
    bool XcodeAvailable,
    string Diagnostic)
{
    public bool HasWorkload(params string[] workloadIds) =>
        workloadIds.Any(InstalledWorkloads.Contains);
}

public static class DotNetBuildEnvironmentProbe
{
    private static readonly Lazy<DotNetBuildEnvironment> Snapshot = new(
        () => InspectCore(ProcessUtility.Current),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static DotNetBuildEnvironment Inspect() => Snapshot.Value;

    internal static DotNetBuildEnvironment Inspect(ProcessUtility processRunner)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        return ReferenceEquals(processRunner, ProcessUtility.Current)
            ? Snapshot.Value
            : InspectCore(processRunner);
    }

    private static DotNetBuildEnvironment InspectCore(ProcessUtility processRunner)
    {
        var version = RunProbe(processRunner, "dotnet", ["--version"], TimeSpan.FromSeconds(10));
        if (!version.Succeeded)
            return new DotNetBuildEnvironment(false, string.Empty,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                ResolveAndroidSdk(), ResolveJavaHome(processRunner), ResolveXcode(processRunner), version.Output);

        var workloadList = RunProbe(
            processRunner, "dotnet", ["workload", "list"], TimeSpan.FromSeconds(20));
        var workloads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (workloadList.Succeeded)
        {
            foreach (Match match in Regex.Matches(workloadList.Output,
                         @"(?m)^\s*(?<id>[a-z][a-z0-9.-]*)\s+"))
                workloads.Add(match.Groups["id"].Value);
        }

        return new DotNetBuildEnvironment(
            true,
            version.Output.Trim(),
            workloads,
            ResolveAndroidSdk(),
            ResolveJavaHome(processRunner),
            ResolveXcode(processRunner),
            workloadList.Succeeded ? string.Empty : workloadList.Output);
    }

    private static string? ResolveAndroidSdk()
    {
        foreach (var variable in new[] { "ANDROID_SDK_ROOT", "ANDROID_HOME" })
        {
            var value = EditorHostServices.Platform.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value) && EditorHostServices.FileSystem.DirectoryExists(value)) return EditorHostServices.Paths.GetFullPath(value);
        }

        var local = EditorHostServices.Platform.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var home = EditorHostServices.Platform.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var candidate in new[]
                 {
                     EditorHostServices.Paths.Combine(local, "Android", "Sdk"),
                     EditorHostServices.Paths.Combine(home, "Library", "Android", "sdk"),
                     EditorHostServices.Paths.Combine(home, "Android", "Sdk")
                 })
            if (EditorHostServices.FileSystem.DirectoryExists(candidate)) return EditorHostServices.Paths.GetFullPath(candidate);
        return null;
    }

    private static string? ResolveJavaHome(ProcessUtility processRunner)
    {
        var configured = EditorHostServices.Platform.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(configured) && EditorHostServices.FileSystem.DirectoryExists(configured))
            return EditorHostServices.Paths.GetFullPath(configured);
        var command = EditorHostServices.Platform.IsWindows ? "where.exe" : "which";
        var result = RunProbe(processRunner, command, [EditorHostServices.Platform.IsWindows ? "java.exe" : "java"],
            TimeSpan.FromSeconds(5));
        if (!result.Succeeded) return null;
        var executable = result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(executable)) return null;
        var bin = EditorHostServices.Paths.GetDirectoryName(EditorHostServices.Paths.GetFullPath(executable));
        return bin is null ? null : EditorHostServices.Paths.GetDirectoryName(bin);
    }

    private static bool ResolveXcode(ProcessUtility processRunner)
    {
        if (!EditorHostServices.Platform.IsMacOS) return false;
        return RunProbe(processRunner, "xcode-select", ["-p"], TimeSpan.FromSeconds(5)).Succeeded;
    }

    private static ProbeResult RunProbe(
        ProcessUtility processRunner,
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout)
    {
        try
        {
            var request = new ProcessRunRequest(fileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments) request.Arguments.Add(argument);
            if (Path.GetFileNameWithoutExtension(fileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                BEngine.Tooling.ToolEnvironment.ApplyToDotNetBuild(request.EnvironmentVariables);
            using var timeoutSource = new CancellationTokenSource(timeout);
            var result = processRunner.Run(request, timeoutSource.Token);
            return new ProbeResult(result.Succeeded,
                (result.StandardOutput + result.StandardError).Trim());
        }
        catch (OperationCanceledException)
        {
            return new ProbeResult(false,
                $"'{fileName}' did not finish within {timeout.TotalSeconds:0} seconds.");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            return new ProbeResult(false, exception.Message);
        }
    }

    private readonly record struct ProbeResult(bool Succeeded, string Output);
}

internal static partial class DotNetBuildHostLocator
{
    private static readonly IReadOnlyDictionary<BuildTargetPlatform, (string Folder, string Project)> Hosts =
        new Dictionary<BuildTargetPlatform, (string, string)>
        {
            [BuildTargetPlatform.Windows] = ("Desktop", "BEngine.Player.DesktopHost.csproj"),
            [BuildTargetPlatform.Linux] = ("Desktop", "BEngine.Player.DesktopHost.csproj"),
            [BuildTargetPlatform.MacOS] = ("Desktop", "BEngine.Player.DesktopHost.csproj"),
            [BuildTargetPlatform.Android] = ("Android", "BEngine.Player.AndroidHost.csproj"),
            [BuildTargetPlatform.IOS] = ("iOS", "BEngine.Player.iOSHost.csproj"),
            [BuildTargetPlatform.Web] = ("Web", "BEngine.Player.WebHost.csproj")
        };

    internal static bool TryResolve(BuildTargetDescriptor target, string? overridePath, out string projectPath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            projectPath = EditorHostServices.Paths.GetFullPath(overridePath);
            return EditorHostServices.FileSystem.FileExists(projectPath);
        }

        var host = Hosts[target.Platform];
        foreach (var start in new[] { EditorHostServices.Platform.BaseDirectory, EditorHostServices.Platform.CurrentDirectory })
        for (var directory = EditorHostServices.Paths.GetFullPath(start); directory is not null;
             directory = EditorHostServices.Paths.GetDirectoryName(directory))
        {
            foreach (var candidate in new[]
                     {
                         EditorHostServices.Paths.Combine(directory, "src", "Platform", "Hosts",
                             host.Folder, host.Project),
                         EditorHostServices.Paths.Combine(directory, "Engine", "src", "Platform", "Hosts",
                             host.Folder, host.Project),
                         EditorHostServices.Paths.Combine(directory, "BuildHosts", host.Folder, host.Project)
                     })
                if (EditorHostServices.FileSystem.FileExists(candidate))
                {
                    projectPath = candidate;
                    return true;
                }
        }

        projectPath = string.Empty;
        return false;
    }

    internal static string Resolve(BuildTargetDescriptor target, string? overridePath)
    {
        if (TryResolve(target, overridePath, out var projectPath)) return projectPath;
        if (!string.IsNullOrWhiteSpace(overridePath))
            throw new FileNotFoundException("The Player Host project was not found.", EditorHostServices.Paths.GetFullPath(overridePath));
        var host = Hosts[target.Platform];
        throw new FileNotFoundException(
            $"Could not locate the {target.Platform} Player Host '{host.Project}'. " +
            "Re-export the engine with BuildHosts or set PlayerBuildRequest.PlayerHostProjectPath.");
    }

    internal static string? ResolveExportedEngineRoot(string hostProject)
    {
        for (var directory = EditorHostServices.Paths.GetDirectoryName(hostProject); directory is not null;
             directory = EditorHostServices.Paths.GetDirectoryName(directory))
        {
            var buildRuntime = EditorHostServices.Paths.Combine(directory, "BuildRuntime");
            if (EditorHostServices.FileSystem.FileExists(EditorHostServices.Paths.Combine(buildRuntime, "BEngine.Player.dll"))) return buildRuntime;
            if (EditorHostServices.FileSystem.FileExists(EditorHostServices.Paths.Combine(directory, "BEngine.Player.dll"))) return directory;
        }
        var applicationBuildRuntime = EditorHostServices.Paths.Combine(EditorHostServices.Platform.BaseDirectory, "BuildRuntime");
        if (EditorHostServices.FileSystem.FileExists(EditorHostServices.Paths.Combine(applicationBuildRuntime, "BEngine.Player.dll")))
            return applicationBuildRuntime;
        return EditorHostServices.FileSystem.FileExists(EditorHostServices.Paths.Combine(EditorHostServices.Platform.BaseDirectory, "BEngine.Player.dll"))
            ? EditorHostServices.Platform.BaseDirectory
            : null;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex MsBuildPropertyNamePattern();

    internal static void AddAdditionalProperties(ProcessRunRequest process, PlayerBuildRequest request)
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
            "BEngineWebPrecompiledProps", "BEngineWebPrecompiledManifest",
            "BaseOutputPath", "BaseIntermediateOutputPath", "MSBuildProjectExtensionsPath",
            "BEngineTrimmerRootDescriptor", "BEngineHostAssemblyBootstrap", "BEngineFullTrimming", "BEngineIncludeTextBindings", "BEngineWebFontBackend", "TrimMode", "JsonSerializerIsReflectionEnabledByDefault", "ApplicationId", "SupportedOSPlatformVersion",
            "AndroidPackageFormat", "BEnginePlayerPlatform", "CodesignKey", "CodesignProvision",
            "BEngineWindowsGarbageCollection", "BEngineWindowsGcHeapCount"
        };
        foreach (var (name, value) in request.AdditionalMsBuildProperties.OrderBy(pair => pair.Key,
                     StringComparer.Ordinal))
        {
            var overridesWindowsGc = request.WindowsGarbageCollection != PlayerGarbageCollectionMode.RuntimeDefault &&
                request.TargetId.StartsWith("windows", StringComparison.OrdinalIgnoreCase) &&
                name.Equals("ServerGarbageCollection", StringComparison.OrdinalIgnoreCase);
            var overridesWebPrecompilation = request.WebScriptCompilation == WebScriptCompilationMode.AheadOfTime &&
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
        PlayerBuildContext context,
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
        process.Arguments.Add("-p:DebugType=embedded");
        process.Arguments.Add("-p:PublishAot=false");
        if (context.Target.HasBuiltInPlayerHost)
        {
            process.Arguments.Add($"-p:BEngineDesktopGraphics={PlayerGraphicsBuildSettings.DesktopVariant(context.GraphicsBackends)}");
            process.Arguments.Add($"-p:BEngineRuntimeShaderCompiler={context.Request.RuntimeShaderCompilation.ToString().ToLowerInvariant()}");
        }
        var fullTrimming = context.Request.ManagedStripping == PlayerManagedStrippingLevel.Full;
        if (fullTrimming && !context.Request.SelfContained)
            throw new InvalidDataException("Full Player stripping requires a self-contained build.");
        var trimManagedCode = context.Request.SelfContained &&
                              context.Request.ManagedStripping != PlayerManagedStrippingLevel.Disabled;
        process.Arguments.Add($"-p:BEngineFullTrimming={fullTrimming.ToString().ToLowerInvariant()}");
        var linkDescriptors = PlayerStrippingSettings.GetLinkDescriptors(
            BEngine.ProjectSystem.ProjectWorkspace.Open(context.Request.ProjectPath));
        var includeTextBindings = !fullTrimming || PlayerHostContractRoots.RequiresTextBindings(
            context.RuntimeManagedCodeAssemblyPaths, new[] { context.ReflectionRootDescriptorPath }.Concat(linkDescriptors));
        process.Arguments.Add($"-p:BEngineIncludeTextBindings={includeTextBindings.ToString().ToLowerInvariant()}");
        context.IncludeTextBindings = includeTextBindings;
        if (context.StrippingReport is { } featureReport) featureReport.TextBindingsIncluded = includeTextBindings;
        var hostBootstrap = PlayerHostAssemblyBootstrap.Write(
            context.RuntimeManagedCodeAssemblyPaths.Concat(PlayerSerializationRoots.HostAssemblyPaths),
            pathService.Combine(buildArtifactsDirectory, "BEngineStaticHostBootstrap.cs"), includeTextBindings,
            includeSkiaBindings: context.Target.Platform != BuildTargetPlatform.Web ||
                                 context.Request.WebFontBackend != WebFontBackend.FreeType,
            startupLogoPng: context.StartupLogoPng.ToArray(), splashBackground: context.Request.SplashBackgroundColor);
        process.Arguments.Add($"-p:BEngineHostAssemblyBootstrap={hostBootstrap}");
        process.Arguments.Add($"-p:PublishTrimmed={trimManagedCode.ToString().ToLowerInvariant()}");
        if (trimManagedCode)
        {
            process.Arguments.Add(fullTrimming ? "-p:TrimMode=full" : "-p:TrimMode=partial");
            process.Arguments.Add(fullTrimming ? "-p:SuppressTrimAnalysisWarnings=false" : "-p:SuppressTrimAnalysisWarnings=true");
            if (fullTrimming)
            {
                process.Arguments.Add("-p:TrimmerSingleWarn=false");
                process.Arguments.Add("-p:ILLinkTreatWarningsAsErrors=false");
                // Keep analyzer/linker diagnostics in the publish log; the engine's
                // reflection and native interop contracts are validated by the player smoke tests.
                process.Arguments.Add("-p:TreatWarningsAsErrors=false");
                // Core prefab graphs and existing reloadable game serializers use
                // reflection; their DTO members are described by PlayerSerializationRoots.
                process.Arguments.Add("-p:JsonSerializerIsReflectionEnabledByDefault=true");
            }
        }
        // WebAssembly and mobile targets may require trimming/AOT even when the project's
        // stripping option is disabled. Always describe the dynamic release boundary so a
        // platform-specific PublishTrimmed=true cannot remove framework contracts used only
        // by interpreted package and game assemblies.
        var preciseHostRootsReady = false;
        if (context.Request.SelfContained && context.RuntimeManagedCodeAssemblyPaths.Count > 0)
        {
            var descriptorPath = DynamicManagedCodeTrimmerDescriptor.Write(
                EditorHostServices.Paths.Combine(buildArtifactsDirectory, "bengine-dynamic-dependencies.xml"),
                context.RuntimeManagedCodeAssemblyPaths, fullTrimming);
            var descriptor = XDocument.Load(descriptorPath);
            PlayerLinkXml.MergeInto(descriptor.Root!, linkDescriptors,
                context.RuntimeManagedCodeAssemblyPaths.Concat(PlayerSerializationRoots.HostAssemblyPaths)
                    .Concat((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
                        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)));
            if (fullTrimming)
            {
                PlayerSerializationRoots.Add(descriptor.Root!, PlayerSerializationRoots.HostAssemblyPaths
                    .Concat(context.RuntimeManagedCodeAssemblyPaths));
                var dynamicNames = context.RuntimeManagedCodeAssemblyPaths.Select(Path.GetFileNameWithoutExtension)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var assembly in descriptor.Root!.Elements("assembly").ToArray())
                {
                    var name = ((string)assembly.Attribute("fullname")!).Split(',')[0];
                    if (dynamicNames.Contains(name) && !DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(name)) assembly.Remove();
                }
                foreach (var additional in new[] { context.ReflectionRootDescriptorPath })
                {
                    if (additional is null) continue;
                    foreach (var assembly in XDocument.Load(additional).Root!.Elements("assembly"))
                    {
                        var copy = new XElement(assembly);
                        var name = ((string)copy.Attribute("fullname")!).Split(',')[0];
                        if (dynamicNames.Contains(name) && !DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(name)) continue;
                        if (!descriptor.Root!.Elements("assembly").Any(existing => XNode.DeepEquals(existing, copy)))
                            descriptor.Root!.Add(copy);
                    }
                }
                descriptor.Save(descriptorPath);
                preciseHostRootsReady = true;
                if (context.StrippingReportPath is { } rootReportPath)
                    fileSystem.WriteAllText(Path.ChangeExtension(rootReportPath, ".host-roots.xml"),
                        fileSystem.ReadAllText(descriptorPath));
            }
            else descriptor.Save(descriptorPath);
            process.Arguments.Add($"-p:BEngineTrimmerRootDescriptor={descriptorPath}");
        }
        process.Arguments.Add("-p:NuGetAudit=false");
        process.Arguments.Add("-p:RestoreIgnoreFailedSources=true");
        var engineRoot = DotNetBuildHostLocator.ResolveExportedEngineRoot(projectPath);
        if (engineRoot is not null) process.Arguments.Add($"-p:BEngineEngineRoot={engineRoot}");
        if (context.Target.Platform == BuildTargetPlatform.Web &&
            context.Request.WebScriptCompilation == WebScriptCompilationMode.AheadOfTime &&
            context.WebPrecompiledManagedCodeAssemblyPaths.Count > 0)
            process.Arguments.Add($"-p:BEngineWebPrecompiledProps={WebPrecompiledScriptBuild.Write(context, buildArtifactsDirectory, preciseHostRootsReady)}");
        configure?.Invoke(process);
        DotNetBuildHostLocator.AddAdditionalProperties(process, context.Request);
        BEngine.Tooling.ToolEnvironment.ApplyToDotNetBuild(process.EnvironmentVariables);

        try
        {
            var result = await processRunner.RunAsync(process, cancellationToken).ConfigureAwait(false);
            if (context.StrippingReport is { } report && context.StrippingReportPath is { } reportPath)
            {
                report.HostPublishLogPath = Path.ChangeExtension(reportPath, ".publish.log");
                fileSystem.WriteAllText(report.HostPublishLogPath, result.StandardOutput + result.StandardError);
                var originalDirectory = pathService.Combine(buildArtifactsDirectory, "bin");
                var original = (Directory.Exists(originalDirectory)
                        ? Directory.EnumerateFiles(originalDirectory, "*.dll", SearchOption.AllDirectories) : [])
                    .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(static group => group.Key!, static group => group.Max(path => new FileInfo(path).Length), StringComparer.OrdinalIgnoreCase);
                var linked = Directory.EnumerateFiles(buildArtifactsDirectory, "*.dll", SearchOption.AllDirectories)
                    .Where(static path => Path.GetDirectoryName(path)!.EndsWith("linked", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(static group => group.Key!, static group => group.Max(path => new FileInfo(path).Length), StringComparer.OrdinalIgnoreCase);
                foreach (var entry in original.OrderBy(static p => p.Key, StringComparer.Ordinal))
                {
                    var retained = linked.TryGetValue(entry.Key, out var size);
                    report.HostAssemblies.Add(new PlayerStrippingReportEntry { Name = entry.Key,
                        OriginalBytes = entry.Value, PublishedBytes = size, Included = retained,
                        Reason = retained ? "Host call graph or dynamic-code contract" : "No reachable Host dependency" });
                }
                fileSystem.WriteAllText(reportPath, YamlUtility.Serialize(report));
            }
            if (result.Succeeded)
            {
                var contractAssemblies = Directory.EnumerateFiles(buildArtifactsDirectory, "*.dll", SearchOption.AllDirectories)
                    .Where(static path => Path.GetDirectoryName(path)!.EndsWith("linked", StringComparison.OrdinalIgnoreCase))
                    .Where(IsManagedAssembly).ToArray();
                if (contractAssemblies.Length == 0)
                    contractAssemblies = Directory.EnumerateFiles(Path.Combine(buildArtifactsDirectory, "bin"), "*.dll", SearchOption.AllDirectories)
                        .Where(IsManagedAssembly)
                        .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Select(static g => g.First()).ToArray();
                if (contractAssemblies.Length > 0)
                {
                    context.HostContractPath = PlayerHostContract.Write(context, contractAssemblies);
                    if (context.StrippingReport is { } contractReport && context.StrippingReportPath is { } contractReportPath)
                    {
                        contractReport.HostContractPath = context.HostContractPath;
                        fileSystem.WriteAllText(contractReportPath, YamlUtility.Serialize(contractReport));
                    }
                }
            }
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

    private static bool IsManagedAssembly(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            return reader.HasMetadata;
        }
        catch (IOException) { return false; }
        catch (BadImageFormatException) { return false; }
    }

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
