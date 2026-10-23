using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace BEngine.Launcher;

/// <summary>Reuses installed x64 .NET 10 before offering Microsoft's interactive installer.</summary>
internal sealed class DotNetEnvironmentService : IDisposable
{
    private static readonly Uri ReleasesUri = new("https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json");
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _cacheRoot;
    private readonly Func<CancellationToken, Task<DotNetEnvironmentSnapshot>> _probe;
    private readonly Func<string, CancellationToken, Task<int>> _installer;
    private readonly SemaphoreSlim _installationGate = new(1, 1);

    internal DotNetEnvironmentService(string installationRoot, HttpClient? httpClient = null,
        Func<CancellationToken, Task<DotNetEnvironmentSnapshot>>? probe = null,
        Func<string, CancellationToken, Task<int>>? installer = null)
    {
        _cacheRoot = Path.Combine(LauncherInstallationPaths.ResolveTemporaryDirectory(installationRoot), "Hub", "Prerequisites");
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _probe = probe ?? ProbeInstalledAsync;
        _installer = installer ?? RunInstallerAsync;
    }

    internal Task<DotNetEnvironmentSnapshot> ProbeAsync(CancellationToken cancellationToken = default) => _probe(cancellationToken);

    internal async Task<DotNetEnvironmentInstallResult> EnsureReadyAsync(
        IProgress<DotNetEnvironmentProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await _installationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            progress?.Report(new("Checking", "Checking installed .NET 10 SDK and Desktop Runtime (x64)"));
            var snapshot = await _probe(cancellationToken).ConfigureAwait(false);
            // No network access or installer launch when an existing compatible environment is usable.
            if (snapshot.IsReady)
            {
                progress?.Report(new("Ready", "Using the installed .NET environment", Percent: 100));
                return new(snapshot);
            }

            var installed = false;
            var restartRequired = false;
            // The Windows SDK installer includes Desktop Runtime, so install it first if needed.
            // Probe again afterward rather than unnecessarily downloading both installers.
            for (var attempt = 0; attempt < 2 && !snapshot.IsReady; attempt++)
            {
                var installSdk = !snapshot.HasSdk;
                progress?.Report(new("Metadata", installSdk ? "Finding the .NET 10 SDK installer" : "Finding the .NET 10 Desktop Runtime installer"));
                var package = await GetInstallerAsync(installSdk, cancellationToken).ConfigureAwait(false);
                var workspace = Path.Combine(_cacheRoot, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workspace);
                try
                {
                    var installerPath = Path.Combine(workspace, installSdk ? "dotnet-sdk-win-x64.exe" : "windowsdesktop-runtime-win-x64.exe");
                    await DownloadInstallerAsync(package, installerPath, progress, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new("Installing", "Complete the Microsoft installer; you can cancel in its window"));
                    var exitCode = await _installer(installerPath, cancellationToken).ConfigureAwait(false);
                    if (exitCode is 1602 or 1223)
                    {
                        snapshot = await _probe(CancellationToken.None).ConfigureAwait(false);
                        progress?.Report(new("Cancelled", "The .NET installer was cancelled"));
                        return new(snapshot, installed, restartRequired, Cancelled: true);
                    }
                    if (exitCode is not (0 or 3010 or 1641))
                        throw new IOException($"The Microsoft .NET installer failed (exit code {exitCode}).");
                    installed = true;
                    restartRequired |= exitCode is 3010 or 1641;
                    progress?.Report(new("Checking", "Verifying the installed .NET environment"));
                    snapshot = await _probe(CancellationToken.None).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (restartRequired && !snapshot.IsReady)
                    {
                        progress?.Report(new("RestartRequired", "Restart Windows to finish installing .NET"));
                        return new(snapshot, installed, RestartRequired: true);
                    }
                    if (installSdk && !snapshot.HasSdk || !installSdk && !snapshot.HasDesktopRuntime)
                        throw new IOException("The installer completed, but the required x64 .NET 10 environment could not be verified. Check the Microsoft installer log or restart Windows.");
                }
                finally
                {
                    // This directory belongs only to this operation; preserve all other Hub/engine Temp data.
                    TryRemoveWorkspace(workspace);
                }
            }
            if (!snapshot.IsReady) throw new IOException("The required .NET 10 SDK and Desktop Runtime (x64) are still unavailable.");
            progress?.Report(new("Ready", "The .NET environment is ready", Percent: 100));
            return new(snapshot, installed, restartRequired);
        }
        finally
        {
            _installationGate.Release();
        }
    }

    internal static async Task<DotNetEnvironmentSnapshot> ProbeInstalledAsync(CancellationToken cancellationToken = default)
    {
        var installations = new List<DotNetEnvironmentSnapshot>();
        foreach (var root in EnumerateInstallationRoots())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var executable = Path.Combine(root, "dotnet.exe");
            if (!IsX64Executable(executable)) continue;
            try
            {
                var runtimes = await ReadCliOutputAsync(executable, "--list-runtimes", cancellationToken).ConfigureAwait(false);
                var sdks = await ReadCliOutputAsync(executable, "--list-sdks", cancellationToken).ConfigureAwait(false);
                var installation = ParseInstallation(root, runtimes, sdks);
                // Listing SDK directories alone does not prove that their required Core patch
                // is installed. Execute the selected SDK entry point directly so a newer SDK
                // in the same root (or a nearby global.json) cannot mask a broken .NET 10 SDK.
                var usableSdk = await ProbeSdkAsync(executable, root, sdks, cancellationToken).ConfigureAwait(false);
                installations.Add(installation with { SdkVersion = usableSdk, SdkRoot = usableSdk is null ? null : root });
            }
            catch (Exception exception) when (exception is IOException or Win32Exception or UnauthorizedAccessException)
            {
                // A broken/stale candidate must not hide a usable installation in a different location.
            }
        }
        // Prefer one complete root. Separate valid roots are also usable by setting DOTNET_ROOT_X64
        // to RuntimeRoot and placing SdkRoot first on the child process PATH.
        var complete = installations.FirstOrDefault(item => item.IsReady);
        if (complete is not null) return complete;
        var runtime = installations.FirstOrDefault(item => item.HasDesktopRuntime);
        var sdk = installations.FirstOrDefault(item => item.HasSdk);
        return new(runtime?.DesktopRuntimeVersion, sdk?.SdkVersion, runtime?.RuntimeRoot, sdk?.SdkRoot);
    }

    internal static DotNetEnvironmentSnapshot ParseInstallation(string root, string runtimes, string sdks)
    {
        var coreRuntimes = RuntimeVersions(runtimes, "Microsoft.NETCore.App").ToArray();
        var hasCoreRuntime = coreRuntimes.Length != 0;
        var desktop = RuntimeVersions(runtimes, "Microsoft.WindowsDesktop.App")
            .Where(version => coreRuntimes.Any(core => core >= version)).OrderDescending().FirstOrDefault();
        var sdk = hasCoreRuntime ? sdks.Split('\n').Select(line => line.Trim().Split(' ', 2)[0])
            .Select(value => CompatibleVersion(value, sdk: true)).OfType<Version>().OrderDescending().FirstOrDefault() : null;
        return new(desktop?.ToString(), sdk?.ToString(), desktop is not null ? root : null, sdk is not null ? root : null);
    }

    internal static Version? CompatibleVersion(string value, bool sdk)
    {
        // Default .NET apphost roll-forward does not accept a different major version or previews.
        return Version.TryParse(value, out var version) && version.Major == 10 && version.Minor == 0 &&
            version.Build >= (sdk ? 100 : 0) && version.Revision <= 0 ? version : null;
    }

    private static IEnumerable<Version> RuntimeVersions(string output, string framework)
    {
        foreach (var line in output.Split('\n'))
        {
            var parts = line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0] == framework && CompatibleVersion(parts[1], sdk: false) is { } version)
                yield return version;
        }
    }

    private static async Task<string?> ProbeSdkAsync(string executable, string root, string output, CancellationToken cancellationToken)
    {
        var versions = output.Split('\n').Select(line => line.Trim().Split(' ', 2)[0])
            .Select(value => CompatibleVersion(value, sdk: true)).OfType<Version>().Distinct().OrderDescending();
        foreach (var version in versions)
        {
            var sdkRoot = Path.Combine(root, "sdk", version.ToString());
            var entryPoint = Path.Combine(sdkRoot, "dotnet.dll");
            if (!File.Exists(entryPoint) || !File.Exists(Path.Combine(sdkRoot, "dotnet.runtimeconfig.json")) ||
                !File.Exists(Path.Combine(sdkRoot, "MSBuild.dll"))) continue;
            try
            {
                var actual = await ReadCliOutputAsync(executable, [entryPoint, "--version"], cancellationToken).ConfigureAwait(false);
                if (CompatibleVersion(actual.Trim(), sdk: true) == version) return version.ToString();
            }
            catch (Exception exception) when (exception is IOException or Win32Exception or UnauthorizedAccessException) { }
        }
        return null;
    }

    private static IReadOnlyList<string> EnumerateInstallationRoots()
    {
        var roots = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            try
            {
                var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'))));
                if (seen.Add(root)) roots.Add(root);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        Add(Environment.GetEnvironmentVariable("DOTNET_ROOT_X64"));
        Add(Environment.GetEnvironmentVariable("DOTNET_ROOT"));
        var currentRuntime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        if (currentRuntime.Parent?.Name == "Microsoft.NETCore.App" && currentRuntime.Parent.Parent?.Name == "shared")
            Add(currentRuntime.Parent.Parent.Parent?.FullName);
        // Microsoft's registered install location uses the 32-bit registry view, including x64 installs.
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = registry.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64");
                Add(key?.GetValue("InstallLocation") as string);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
        var programFiles = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Add(Path.Combine(programFiles, "dotnet"));
        Add(Path.Combine(programFiles, "dotnet", "x64")); // x64 .NET on Windows Arm64.
        foreach (var target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
        {
            var path = Environment.GetEnvironmentVariable("PATH", target);
            foreach (var part in (path ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) Add(part);
        }
        return roots;
    }

    private static bool IsX64Executable(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 64 || reader.ReadUInt16() != 0x5a4d) return false;
            stream.Position = 0x3c;
            var offset = reader.ReadInt32();
            if (offset < 64 || offset > stream.Length - 6) return false;
            stream.Position = offset;
            return reader.ReadUInt32() == 0x00004550 && reader.ReadUInt16() == 0x8664;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    private static Task<string> ReadCliOutputAsync(string executable, string argument, CancellationToken cancellationToken) =>
        ReadCliOutputAsync(executable, [argument], cancellationToken);

    private static async Task<string> ReadCliOutputAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new(executable)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(executable)!
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        BEngine.Tooling.ToolEnvironment.ApplyToDotNetBuild(process.StartInfo.Environment);
        process.StartInfo.Environment["DOTNET_NOLOGO"] = "1";
        process.StartInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        if (!process.Start()) throw new IOException("Could not inspect the .NET installation.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await errors.ConfigureAwait(false);
            var text = await output.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException("The .NET installation could not report its installed components.");
            return text;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKillProbe(process);
            throw new IOException("Inspecting the .NET installation timed out.");
        }
        catch (OperationCanceledException)
        {
            TryKillProbe(process);
            throw;
        }
    }

    private async Task<DotNetEnvironmentInstaller> GetInstallerAsync(bool sdk, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            using var response = await _http.GetAsync(ReleasesUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var metadata = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
            return SelectInstaller(metadata.RootElement, sdk);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("Reading Microsoft's .NET release information timed out. Check the network and try again.");
        }
    }

    internal static DotNetEnvironmentInstaller SelectInstaller(JsonElement metadata, bool sdk)
    {
        if (metadata.GetProperty("channel-version").GetString() != "10.0") throw new InvalidDataException("Unexpected .NET release channel.");
        var candidates = new List<DotNetEnvironmentInstaller>();
        foreach (var release in metadata.GetProperty("releases").EnumerateArray())
        {
            if (release.TryGetProperty(sdk ? "sdk" : "windowsdesktop", out var component)) AddComponent(component);
            if (sdk && release.TryGetProperty("sdks", out var sdks))
                foreach (var item in sdks.EnumerateArray()) AddComponent(item);
        }
        return candidates.OrderByDescending(item => Version.Parse(item.Version)).FirstOrDefault() ??
            throw new InvalidDataException("Microsoft's release information contains no stable .NET 10 Windows x64 installer.");

        void AddComponent(JsonElement component)
        {
            var version = component.GetProperty("version").GetString() ?? string.Empty;
            if (CompatibleVersion(version, sdk) is null || !component.TryGetProperty("files", out var files)) return;
            foreach (var file in files.EnumerateArray())
            {
                if (file.GetProperty("rid").GetString() != "win-x64" ||
                    file.GetProperty("name").GetString() != (sdk ? "dotnet-sdk-win-x64.exe" : "windowsdesktop-runtime-win-x64.exe")) continue;
                var uri = new Uri(file.GetProperty("url").GetString() ?? string.Empty, UriKind.Absolute);
                ValidateDownloadUri(uri);
                var hash = file.GetProperty("hash").GetString() ?? string.Empty;
                if (hash.Length != 128 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("The .NET installer SHA-512 hash is invalid.");
                candidates.Add(new(version, uri, hash, sdk));
            }
        }
    }

    private async Task DownloadInstallerAsync(DotNetEnvironmentInstaller package, string path,
        IProgress<DotNetEnvironmentProgress>? progress, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            using var response = await _http.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is { } finalUri) ValidateDownloadUri(finalUri);
            var length = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
            var buffer = new byte[81920];
            long received = 0;
            long lastReport = 0;
            progress?.Report(new("Downloading", $"Downloading .NET {package.Version} (x64)", Total: length ?? 0));
            while (true)
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                var count = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                if (count == 0) break;
                await destination.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                hash.AppendData(buffer, 0, count);
                received += count;
                if (Stopwatch.GetElapsedTime(lastReport).TotalMilliseconds < 100) continue;
                lastReport = Stopwatch.GetTimestamp();
                progress?.Report(new("Downloading", $"Downloading .NET {package.Version} (x64)", received, length ?? 0,
                    length is > 0 ? received * 100d / length.Value : null));
            }
            progress?.Report(new("Verifying", "Verifying the Microsoft installer SHA-512 hash", received, length ?? received, 100));
            if (length.HasValue && received != length.Value) throw new InvalidDataException("The .NET installer download is incomplete.");
            if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(package.Sha512)))
                throw new InvalidDataException("The .NET installer failed SHA-512 verification. The installer was not started.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("Downloading the .NET installer timed out. Check the network and try again.");
        }
    }

    private static void ValidateDownloadUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
            uri.Host is not ("builds.dotnet.microsoft.com" or "download.visualstudio.microsoft.com" or "dotnetcli.azureedge.net"))
            throw new InvalidDataException("The .NET installer download URL is not an approved Microsoft HTTPS endpoint.");
    }

    private static async Task<int> RunInstallerAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var start = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! };
            foreach (var argument in new[] { "/install", "/norestart", "/log", Path.Combine(Path.GetDirectoryName(path)!, "install.log") })
                start.ArgumentList.Add(argument);
            BEngine.Tooling.ToolEnvironment.ApplyToDotNetBuild(start.Environment);
            using var process = Process.Start(start) ??
                throw new IOException("The Microsoft .NET installer could not be opened.");
            // Let Microsoft's own Cancel action roll back installation. Killing an MSI process can leave
            // system components half configured, so cancellation never terminates an active installer.
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223) { return 1223; }
    }

    private static void TryKillProbe(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception) { }
    }

    private static void TryRemoveWorkspace(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
        _installationGate.Dispose();
    }
}
