using System.Text.RegularExpressions;

namespace BEngine.Platform;

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

    public static DotNetBuildEnvironment Inspect(ProcessUtility processRunner)
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

