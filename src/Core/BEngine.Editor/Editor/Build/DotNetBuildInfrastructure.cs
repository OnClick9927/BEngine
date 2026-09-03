using System.Diagnostics;
using System.Text.RegularExpressions;
using BEngine.Build;

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
    private static readonly Lazy<DotNetBuildEnvironment> Snapshot = new(InspectCore,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static DotNetBuildEnvironment Inspect() => Snapshot.Value;

    private static DotNetBuildEnvironment InspectCore()
    {
        var version = RunProbe("dotnet", ["--version"], TimeSpan.FromSeconds(10));
        if (!version.Succeeded)
            return new DotNetBuildEnvironment(false, string.Empty,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                ResolveAndroidSdk(), ResolveJavaHome(), ResolveXcode(), version.Output);

        var workloadList = RunProbe("dotnet", ["workload", "list"], TimeSpan.FromSeconds(20));
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
            ResolveJavaHome(),
            ResolveXcode(),
            workloadList.Succeeded ? string.Empty : workloadList.Output);
    }

    private static string? ResolveAndroidSdk()
    {
        foreach (var variable in new[] { "ANDROID_SDK_ROOT", "ANDROID_HOME" })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value)) return Path.GetFullPath(value);
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var candidate in new[]
                 {
                     Path.Combine(local, "Android", "Sdk"),
                     Path.Combine(home, "Library", "Android", "sdk"),
                     Path.Combine(home, "Android", "Sdk")
                 })
            if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
        return null;
    }

    private static string? ResolveJavaHome()
    {
        var configured = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return Path.GetFullPath(configured);
        var command = OperatingSystem.IsWindows() ? "where.exe" : "which";
        var result = RunProbe(command, [OperatingSystem.IsWindows() ? "java.exe" : "java"],
            TimeSpan.FromSeconds(5));
        if (!result.Succeeded) return null;
        var executable = result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(executable)) return null;
        var bin = Path.GetDirectoryName(Path.GetFullPath(executable));
        return bin is null ? null : Directory.GetParent(bin)?.FullName;
    }

    private static bool ResolveXcode()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        return RunProbe("xcode-select", ["-p"], TimeSpan.FromSeconds(5)).Succeeded;
    }

    private static ProbeResult RunProbe(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        try
        {
            var start = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null) return new ProbeResult(false, $"Could not start '{fileName}'.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                return new ProbeResult(false, $"'{fileName}' did not finish within {timeout.TotalSeconds:0} seconds.");
            }
            Task.WaitAll(output, error);
            return new ProbeResult(process.ExitCode == 0, (output.Result + error.Result).Trim());
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
            projectPath = Path.GetFullPath(overridePath);
            return File.Exists(projectPath);
        }

        var host = Hosts[target.Platform];
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        for (var directory = new DirectoryInfo(Path.GetFullPath(start)); directory is not null;
             directory = directory.Parent)
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(directory.FullName, "src", "Core", "BEngine.Player.Hosts",
                             host.Folder, host.Project),
                         Path.Combine(directory.FullName, "BuildHosts", host.Folder, host.Project)
                     })
                if (File.Exists(candidate))
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
            throw new FileNotFoundException("The Player Host project was not found.", Path.GetFullPath(overridePath));
        var host = Hosts[target.Platform];
        throw new FileNotFoundException(
            $"Could not locate the {target.Platform} Player Host '{host.Project}'. " +
            "Re-export the engine with BuildHosts or set PlayerBuildRequest.PlayerHostProjectPath.");
    }

    internal static string? ResolveExportedEngineRoot(string hostProject)
    {
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(hostProject)!); directory is not null;
             directory = directory.Parent)
        {
            var buildRuntime = Path.Combine(directory.FullName, "BuildRuntime");
            if (File.Exists(Path.Combine(buildRuntime, "BEngine.Player.dll"))) return buildRuntime;
            if (File.Exists(Path.Combine(directory.FullName, "BEngine.Player.dll"))) return directory.FullName;
        }
        var applicationBuildRuntime = Path.Combine(AppContext.BaseDirectory, "BuildRuntime");
        if (File.Exists(Path.Combine(applicationBuildRuntime, "BEngine.Player.dll")))
            return applicationBuildRuntime;
        return File.Exists(Path.Combine(AppContext.BaseDirectory, "BEngine.Player.dll"))
            ? AppContext.BaseDirectory
            : null;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex MsBuildPropertyNamePattern();

    internal static void AddAdditionalProperties(ProcessStartInfo start, PlayerBuildRequest request)
    {
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "OutputPath", "PublishDir", "RuntimeIdentifier", "TargetFramework",
            "BEngineContentRoot", "BEngineEngineRoot", "PublishAot", "PublishTrimmed",
            "PublishSingleFile", "IncludeNativeLibrariesForSelfExtract", "UseAppHost",
            "RunAOTCompilation", "WasmBuildNative", "AotAssemblies", "UseInterpreter",
            "AndroidUseInterpreter", "MtouchInterpreter", "ArtifactsPath", "UseArtifactsOutput",
            "BaseOutputPath", "BaseIntermediateOutputPath", "MSBuildProjectExtensionsPath",
            "BEngineTrimmerRootDescriptor", "ApplicationId", "SupportedOSPlatformVersion",
            "AndroidPackageFormat"
        };
        foreach (var (name, value) in request.AdditionalMsBuildProperties.OrderBy(pair => pair.Key,
                     StringComparer.Ordinal))
        {
            if (!MsBuildPropertyNamePattern().IsMatch(name) || reserved.Contains(name))
                throw new ArgumentException($"MSBuild property '{name}' is invalid or reserved.",
                    nameof(request.AdditionalMsBuildProperties));
            if (value.Contains('\r') || value.Contains('\n') || value.IndexOf('\0') >= 0)
                throw new ArgumentException($"MSBuild property '{name}' contains a control character.",
                    nameof(request.AdditionalMsBuildProperties));
            start.ArgumentList.Add($"-p:{name}={value}");
        }
    }
}

internal static class DotNetPublishRunner
{
    internal static async Task RunAsync(
        string projectPath,
        PlayerBuildContext context,
        Action<ProcessStartInfo>? configure = null,
        CancellationToken cancellationToken = default,
        string? outputDirectory = null)
    {
        var buildArtifactsDirectory = Path.Combine(
            Path.GetTempPath(), "BEngine", "PlayerBuildArtifacts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(buildArtifactsDirectory);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(projectPath)!
        };
        start.ArgumentList.Add("publish");
        start.ArgumentList.Add(projectPath);
        start.ArgumentList.Add("--configuration");
        start.ArgumentList.Add(context.Request.Configuration);
        start.ArgumentList.Add("--runtime");
        start.ArgumentList.Add(context.Target.RuntimeIdentifier);
        start.ArgumentList.Add("--self-contained");
        start.ArgumentList.Add(context.Request.SelfContained ? "true" : "false");
        start.ArgumentList.Add("--output");
        start.ArgumentList.Add(outputDirectory ?? context.StagingDirectory);
        start.ArgumentList.Add("--artifacts-path");
        start.ArgumentList.Add(buildArtifactsDirectory);
        start.ArgumentList.Add("--nologo");
        start.ArgumentList.Add("--disable-build-servers");
        start.ArgumentList.Add("-nodeReuse:false");
        start.ArgumentList.Add("-maxcpucount:1");
        start.ArgumentList.Add($"-p:DebugSymbols={context.Request.IncludeDebugSymbols.ToString().ToLowerInvariant()}");
        start.ArgumentList.Add("-p:DebugType=embedded");
        start.ArgumentList.Add("-p:PublishAot=false");
        var trimManagedCode = context.Request.SelfContained &&
                              context.Request.ManagedStripping ==
                              PlayerManagedStrippingLevel.Conservative;
        start.ArgumentList.Add($"-p:PublishTrimmed={trimManagedCode.ToString().ToLowerInvariant()}");
        if (trimManagedCode)
        {
            start.ArgumentList.Add("-p:TrimMode=partial");
            start.ArgumentList.Add("-p:SuppressTrimAnalysisWarnings=true");
            var descriptorPath = DynamicManagedCodeTrimmerDescriptor.Write(
                Path.Combine(buildArtifactsDirectory, "bengine-dynamic-dependencies.xml"),
                context.RuntimeManagedCodeAssemblyPaths);
            start.ArgumentList.Add($"-p:BEngineTrimmerRootDescriptor={descriptorPath}");
        }
        start.ArgumentList.Add("-p:NuGetAudit=false");
        start.ArgumentList.Add("-p:RestoreIgnoreFailedSources=true");
        var engineRoot = DotNetBuildHostLocator.ResolveExportedEngineRoot(projectPath);
        if (engineRoot is not null) start.ArgumentList.Add($"-p:BEngineEngineRoot={engineRoot}");
        configure?.Invoke(start);
        DotNetBuildHostLocator.AddAdditionalProperties(start, context.Request);

        try
        {
            using var process = Process.Start(start) ??
                                throw new InvalidOperationException("Could not start dotnet publish.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            var standardOutput = await output.ConfigureAwait(false);
            var standardError = await error.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"dotnet publish failed for {context.Target.TargetId} ({process.ExitCode}).\n" +
                    standardOutput + standardError);
        }
        finally
        {
            DeleteTemporaryArtifacts(buildArtifactsDirectory);
        }
    }

    private static void DeleteTemporaryArtifacts(string directory)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) when (attempt < 4) { Thread.Sleep(100); }
        }
    }
}
