using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BEngine.Build;

namespace BEngine.Testing.Editor;

public static class PlayerTestSession
{
    /// <summary>Runs an already-built device/browser Player against a prepared main content directory.</summary>
    public static Task<TestRunResult> RunBuiltPlayerAsync(PlayerBuildResult build, string contentDirectory, string resultPath,
        bool collectCoverage = false, string androidApplicationIdentifier = "com.bengine.game", IReadOnlyCollection<string>? testIds = null, CancellationToken cancellationToken = default) =>
        RunBuiltPlayerAsync(build, contentDirectory, resultPath, EditorApplication.projectPath,
            collectCoverage, androidApplicationIdentifier, testIds, cancellationToken);

    /// <summary>Runs an already-built Player for an explicit project, including when no editor project is open.</summary>
    public static async Task<TestRunResult> RunBuiltPlayerAsync(PlayerBuildResult build, string contentDirectory, string resultPath,
        string projectPath, bool collectCoverage = false, string androidApplicationIdentifier = "com.bengine.game", IReadOnlyCollection<string>? testIds = null, CancellationToken cancellationToken = default)
    {
        if (!PlayerTestLaunchers.TryGet(build.Target.TargetId, out var launcher)) throw new NotSupportedException("This target has no device/browser test launcher.");
        await using var server = LoopbackContentServer.Start(contentDirectory);
        var result = await launcher(new(build, server.BaseUrl, resultPath, collectCoverage, testIds)
            { AndroidApplicationIdentifier = androidApplicationIdentifier, ProjectPath = projectPath }, cancellationToken);
        if (result.Coverage is not null) CodeCoverage.Merge(result.Coverage);
        return result;
    }
    public static Task<TestRunResult> RunAsync(
        bool collectCoverage,
        IProgress<PlayerBuildProgress>? progress = null,
        CancellationToken cancellationToken = default) => RunProjectAsync(EditorApplication.projectPath, collectCoverage, progress: progress, cancellationToken: cancellationToken);
    public static async Task<TestRunResult> RunProjectAsync(string projectPath, bool collectCoverage,
        string? targetId = null, IReadOnlyCollection<string>? testIds = null,
        IProgress<PlayerBuildProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            throw new InvalidOperationException("A project must be open to run Player tests.");
        var settings = PlayerBuildSettingsStore.Load(projectPath);
        var scenes = PlayerBuildSettingsStore.GetEnabledScenes(settings);
        if (scenes.Count == 0) throw new InvalidDataException("Build Settings contains no enabled scene.");
        var root = Path.Combine(projectPath, "Temp", "TestRunner");
        Directory.CreateDirectory(root);
        var output = Path.Combine(root, "player-" + Guid.NewGuid().ToString("N"));
        var contentOutput = Path.Combine(root, "content-" + Guid.NewGuid().ToString("N"));
        var resultPath = Path.Combine(root, "result-" + Guid.NewGuid().ToString("N") + ".json");
        var request = new PlayerBuildRequest
        {
            ProjectPath = projectPath,
            OutputDirectory = output,
            TargetId = targetId ?? settings.TargetId,
            GraphicsBackends = targetId is null ? settings.GraphicsBackends.ToArray() : [],
            CompileShaders = settings.CompileShaders,
            RuntimeShaderCompilation = settings.RuntimeShaderCompilation,
            HostContractPath = settings.HostContractPath,
            Configuration = settings.DevelopmentBuild ? "Debug" : "Release",
            DevelopmentBuild = settings.DevelopmentBuild,
            SelfContained = settings.SelfContained || (targetId ?? settings.TargetId).StartsWith("android", StringComparison.OrdinalIgnoreCase),
            WindowsGarbageCollection = settings.WindowsGarbageCollection,
            WindowsGcHeapCount = settings.WindowsGcHeapCount,
            WebScriptCompilation = settings.WebScriptCompilation,
            IncludeDebugSymbols = settings.IncludeDebugSymbols,
            ReplaceExisting = false,
            BuildVersion = settings.BuildVersion,
            HotResourceVersion = settings.HotResourceVersion,
            EnableHotUpdate = settings.EnableHotUpdate,
            ContentUpdatePolicy = settings.ContentUpdatePolicy,
            CompressAssetBundles = settings.CompressAssetBundles,
            ManagedStripping = settings.ManagedStripping,
            WritePlayerLog = true,
            SplashScreenEnabled = false,
            SplashImage = settings.SplashImage,
            SplashBackgroundColor = settings.SplashBackgroundColor,
            SplashMinimumDurationSeconds = 0,
            CacheDirectory = settings.CacheDirectory,
            AndroidApplicationIdentifier = settings.AndroidApplicationIdentifier,
            AndroidMinimumApiLevel = settings.AndroidMinimumApiLevel,
            AndroidBuildAppBundle = false,
            AdditionalMsBuildProperties = new Dictionary<string, string> { ["BEngineTestPlayer"] = "true" },
            IosBundleIdentifier = settings.IosBundleIdentifier,
            IosMinimumVersion = settings.IosMinimumVersion,
            Scenes = scenes
        };
        var build = await PlayerBuildPipeline.BuildAsync(request, progress, cancellationToken)
            .ConfigureAwait(false);
        await PlayerContentUpdatePipeline.BuildAsync(request, contentOutput, progress, cancellationToken)
            .ConfigureAwait(false);
        await using var server = LoopbackContentServer.Start(Path.Combine(contentOutput, "main"));
        if (PlayerTestLaunchers.TryGet(build.Target.TargetId, out var launcher))
        {
            var deviceResult = await launcher(new(build, server.BaseUrl, resultPath, collectCoverage, testIds)
                { AndroidApplicationIdentifier = request.AndroidApplicationIdentifier, ProjectPath = projectPath }, cancellationToken).ConfigureAwait(false);
            if (deviceResult.Coverage is not null) CodeCoverage.Merge(deviceResult.Coverage);
            return deviceResult;
        }
        var executable = FindExecutable(build.OutputDirectory, build.Target.Platform);
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = build.OutputDirectory,
            UseShellExecute = false
        };
        start.Environment["BENGINE_TEST_RUNNER_MODE"] = "player";
        start.Environment["BENGINE_TEST_RUNNER_RESULT"] = resultPath;
        start.Environment["BENGINE_TEST_RUNNER_COVERAGE"] = collectCoverage ? "1" : "0";
        if (testIds is not null) start.Environment["BENGINE_TEST_RUNNER_IDS"] = JsonSerializer.Serialize(testIds);
        start.Environment["BENGINE_AOT_AUTO_CHECK"] = "1";
        start.Environment["BENGINE_AOT_AUTO_CONFIRM_UPDATE"] = "1";
        start.Environment["BENGINE_AOT_AUTO_CONTINUE"] = "1";
        start.Environment["BENGINE_ASSET_BUNDLE_REMOTE_URL"] = server.BaseUrl;
        start.Environment["BENGINE_ASSET_BUNDLE_ALLOW_INSECURE_LOOPBACK"] = "1";
        using var process = Process.Start(start) ??
                            throw new InvalidOperationException("The test Player process could not be started.");
        try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        if (!File.Exists(resultPath))
            throw new InvalidDataException(
                $"Player exited with code {process.ExitCode} without writing a test result: {resultPath}");
        var result = JsonSerializer.Deserialize<TestRunResult>(await File.ReadAllTextAsync(resultPath, cancellationToken)
                         .ConfigureAwait(false)) ??
                     throw new InvalidDataException("Player test result JSON was empty.");
        if (process.ExitCode != 0 && result.Succeeded)
            throw new InvalidDataException($"Player reported successful tests but exited with code {process.ExitCode}.");
        if (result.Coverage is not null) CodeCoverage.Merge(result.Coverage);
        return result;
    }

    private sealed class LoopbackContentServer : IAsyncDisposable
    {
        private readonly string _root;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        private LoopbackContentServer(string root)
        {
            _root = Path.GetFullPath(root);
            if (!Directory.Exists(_root))
                throw new DirectoryNotFoundException("Player test content root was not built: " + _root);
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            BaseUrl = $"http://127.0.0.1:{endpoint.Port}/";
            _loop = ServeAsync();
        }

        internal string BaseUrl { get; }
        internal static LoopbackContentServer Start(string root) => new(root);

        private async Task ServeAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                catch (SocketException) when (_stop.IsCancellationRequested) { break; }
                _ = HandleAsync(client);
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
                    var request = await reader.ReadLineAsync(_stop.Token).ConfigureAwait(false);
                    string? header;
                    do { header = await reader.ReadLineAsync(_stop.Token).ConfigureAwait(false); }
                    while (!string.IsNullOrEmpty(header));
                    var parts = request?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
                    if (parts.Length < 2 || parts[0] != "GET")
                    {
                        await RespondAsync(stream, 405, [], _stop.Token).ConfigureAwait(false);
                        return;
                    }
                    var relative = Uri.UnescapeDataString(new Uri("http://localhost" + parts[1]).AbsolutePath)
                        .TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                    var path = Path.GetFullPath(Path.Combine(_root, relative));
                    if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        !File.Exists(path))
                    {
                        await RespondAsync(stream, 404, [], _stop.Token).ConfigureAwait(false);
                        return;
                    }
                    await RespondAsync(stream, 200, await File.ReadAllBytesAsync(path, _stop.Token)
                        .ConfigureAwait(false), _stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
            }
        }

        private static async Task RespondAsync(
            Stream stream,
            int status,
            byte[] body,
            CancellationToken cancellationToken)
        {
            var reason = status == 200 ? "OK" : status == 404 ? "Not Found" : "Method Not Allowed";
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status} {reason}\r\nContent-Length: {body.Length}\r\nAccess-Control-Allow-Origin: *\r\nCross-Origin-Resource-Policy: cross-origin\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            if (body.Length > 0) await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try { await _loop.ConfigureAwait(false); }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
            _stop.Dispose();
        }
    }

    private static string FindExecutable(string output, BuildTargetPlatform platform)
    {
        if (platform == BuildTargetPlatform.Windows && OperatingSystem.IsWindows())
        {
            var windows = Directory.GetFiles(output, "*.exe", SearchOption.TopDirectoryOnly);
            return windows.Length == 1 ? windows[0] : throw new FileNotFoundException($"Expected one Player executable in '{output}', found {windows.Length}.");
        }
        if (!(platform == BuildTargetPlatform.Linux && OperatingSystem.IsLinux() || platform == BuildTargetPlatform.MacOS && OperatingSystem.IsMacOS()))
            throw new PlatformNotSupportedException($"No local launcher for '{platform}' on this host. Use PlayerTestLaunchers.Register for a device/browser launcher.");
        var files = Directory.GetFiles(output, "BEngine.Player", SearchOption.AllDirectories);
        return files.Length == 1 ? files[0] : throw new FileNotFoundException(
            $"Expected one Player executable in '{output}', found {files.Length}.");
    }
}
