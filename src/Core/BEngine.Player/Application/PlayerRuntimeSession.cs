using BEngine.DependencyInjection;
using BEngine.Build;
using BEngine.Content;
using BEngine.Documents;
using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;

namespace BEngine.Player;

public static class BEnginePlayer
{
    public const string PersistentDataPathEnvironmentVariable = "BENGINE_PLAYER_PERSISTENT_DATA_PATH";
    public const string NativeDiagnosticsEnvironmentVariable = "BENGINE_PLAYER_NATIVE_DIAGNOSTICS";
    private static readonly List<nint> NativeLibraryHandles = [];
    private static int _nativeLibrariesInitialized;

    public static int Run(params string[] args)
    {
        PlayerStartupDiagnostics? diagnostics = null;
        PlayerSplashScreen? splash = null;
        var failurePhase = "BOOTSTRAP";
        try
        {
            var startup = ResolveStartup(args);
            var validation = args.Length != 0 &&
                             args[0].Equals("--validate", StringComparison.OrdinalIgnoreCase);
            var persistentDataPath = Environment.GetEnvironmentVariable(
                PersistentDataPathEnvironmentVariable);
            diagnostics = PlayerStartupDiagnostics.Start(
                startup.ProjectPath,
                startup.Manifest,
                persistentDataPath,
                startup.Manifest?.WritePlayerLog ?? true);
            Console.Error.WriteLine($"BENGINE_PLAYER_LOG|{diagnostics.LogPath}");
            PlayerStartupDiagnostics.Phase("01_BOOTSTRAP_STARTED",
                $"project={startup.ProjectPath};build={startup.Manifest?.BuildVersion ?? "development"}");
            if (!validation && startup.Manifest is not null)
            {
                failurePhase = "SPLASH_SCREEN";
                splash = PlayerSplashScreen.Show(startup.Manifest, AppContext.BaseDirectory);
                PlayerStartupDiagnostics.Phase("01_SPLASH_SHOWN",
                    $"visible={splash.IsVisible};image=" +
                    (startup.Manifest.Version >= 4
                        ? startup.Manifest.SplashImageResource
                        : startup.Manifest.SplashImage));
                splash.WaitForMinimumDuration();
                PlayerStartupDiagnostics.Phase("01_SPLASH_PLAYBACK_COMPLETED");
            }
            InitializeDesktopNativeLibraries(startup.NativeLibraryDirectory);
            failurePhase = "CORE_AOT_BOOTSTRAP";
            PlayerStartupDiagnostics.Phase("02_CORE_AOT_BOOTSTRAP_STARTED");
            var target = BuildTargetManifestSerializer.LoadCurrent();
            RuntimeTypeCache.Warmup();
            PlayerStartupDiagnostics.Phase("02_CORE_AOT_BOOTSTRAP_COMPLETED",
                $"target={target.TargetId};runtime={target.ManagedCodeRuntime}");
            if (validation)
            {
                failurePhase = "VALIDATION";
                Validate(
                    startup.ProjectPath,
                    persistentDataPath: Environment.GetEnvironmentVariable(
                        PersistentDataPathEnvironmentVariable));
                Console.WriteLine($"BENGINE_PLAYER_VALIDATION_OK|{startup.ProjectPath}");
                return 0;
            }
            failurePhase = "PLAYER_COMPOSITION";
            if (startup.Manifest is not null)
            {
                using var staged = PlayerStagedRuntimeCoordinator.Create(
                    startup.ProjectPath, persistentDataPath);
                using var stagedApplication = new PlayerApplication(staged);
                failurePhase = "AOT_GAME_LOOP";
                stagedApplication.Run(() =>
                {
                    splash?.Close();
                    PlayerStartupDiagnostics.Phase("01_SPLASH_CLOSED");
                });
                return 0;
            }
            using var services = new ServiceCollection()
                .AddBEnginePlayer(startup.ProjectPath, persistentDataPath)
                .BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });
            using var application = services.GetRequiredService<PlayerApplication>();
            failurePhase = "GAME_LOOP";
            application.Run();
            return 0;
        }
        catch (Exception exception)
        {
            try
            {
                diagnostics ??= PlayerStartupDiagnostics.StartForPlayerRoot(AppContext.BaseDirectory);
                PlayerStartupDiagnostics.Failure(failurePhase, exception);
                Console.Error.WriteLine($"BENGINE_PLAYER_LOG|{diagnostics.LogPath}");
            }
            catch { }
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            splash?.Dispose();
            diagnostics?.Dispose();
        }
    }

    public static string ResolveDefaultProjectPath(string? playerDirectory = null) =>
        ResolvePackagedStartup(playerDirectory ?? AppContext.BaseDirectory).ProjectPath;

    public static PlayerRuntimeSession CreateRuntimeSession(
        string projectPath,
        bool declareAotInterpreter = false,
        string? persistentDataPath = null)
    {
        if (declareAotInterpreter) AotInterpreterRuntimeHost.DeclareInterpreterEnabled();
        return new PlayerRuntimeSession(projectPath, persistentDataPath);
    }

    public static void Validate(
        string projectPath,
        bool declareAotInterpreter = false,
        string? persistentDataPath = null)
    {
        var workspace = ProjectWorkspace.OpenRuntime(Path.GetFullPath(projectPath));
        if (workspace.RuntimeMetadata?.PlayerBootstrap is not null)
        {
            if (declareAotInterpreter) AotInterpreterRuntimeHost.DeclareInterpreterEnabled();
            using var staged = PlayerStagedRuntimeCoordinator.Create(projectPath, persistentDataPath);
            staged.Validate(TimeSpan.FromMinutes(5));
            return;
        }
        using var session = CreateRuntimeSession(
            projectPath, declareAotInterpreter, persistentDataPath);
        session.Start();
        session.Tick(TimeSpan.Zero);
        session.Stop();
    }

    private static void InitializeDesktopNativeLibraries(string nativeLibraryDirectory)
    {
        if (Interlocked.Exchange(ref _nativeLibrariesInitialized, 1) != 0 ||
            !(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())) return;
        var directories = EnumerateNativeLibraryDirectories(nativeLibraryDirectory)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (OperatingSystem.IsWindows() && directories.Length != 0)
        {
            var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator,
                directories.Append(current)));
        }

        var glfwName = OperatingSystem.IsWindows()
            ? "glfw3.dll"
            : OperatingSystem.IsMacOS() ? "libglfw.3.dylib" : "libglfw.so.3";
        string? loadedPath = null;
        foreach (var directory in directories)
        {
            var candidate = Path.Combine(directory, glfwName);
            if (!File.Exists(candidate) || !NativeLibrary.TryLoad(candidate, out var handle)) continue;
            NativeLibraryHandles.Add(handle);
            loadedPath = candidate;
            break;
        }
        if (Environment.GetEnvironmentVariable(NativeDiagnosticsEnvironmentVariable) == "1")
            Console.Error.WriteLine(
                $"BENGINE_NATIVE_DIAGNOSTICS|base={AppContext.BaseDirectory}|" +
                $"runtime={AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES")}|" +
                $"directories={string.Join(',', directories)}|glfw={loadedPath ?? "not-loaded"}");
    }

    private static IEnumerable<string> EnumerateNativeLibraryDirectories(string nativeLibraryDirectory)
    {
        if (!string.IsNullOrWhiteSpace(nativeLibraryDirectory))
            yield return Path.GetFullPath(nativeLibraryDirectory);
        if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is string nativeDirectories)
            foreach (var directory in nativeDirectories.Split(
                         Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return directory;
        yield return AppContext.BaseDirectory;
        yield return Path.Combine(
            AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
    }

    private static PlayerStartup ResolveStartup(IReadOnlyList<string> args)
    {
        if (args.Count != 0 && args[0].Equals("--validate", StringComparison.OrdinalIgnoreCase))
            return args.Count > 1
                ? new PlayerStartup(Path.GetFullPath(args[1]), AppContext.BaseDirectory, null)
                : ResolvePackagedStartup(AppContext.BaseDirectory);
        return args.Count == 0
            ? ResolvePackagedStartup(AppContext.BaseDirectory)
            : new PlayerStartup(Path.GetFullPath(args[0]), AppContext.BaseDirectory, null);
    }

    private static PlayerStartup ResolvePackagedStartup(string playerDirectory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(playerDirectory));
        var legacyBootstrapPath = Path.Combine(root, PlayerBootstrapManifest.FileName);
        if (File.Exists(legacyBootstrapPath))
            return CreatePackagedStartup(root, PlayerBootstrapManifestSerializer.Load(root));

        var executableName = Environment.ProcessPath is { Length: > 0 } processPath &&
                             Path.GetDirectoryName(Path.GetFullPath(processPath))
                                 ?.Equals(root, StringComparison.OrdinalIgnoreCase) == true
            ? Path.GetFileNameWithoutExtension(processPath)
            : null;
        if (!string.IsNullOrWhiteSpace(executableName))
        {
            var directDataDirectory = Path.Combine(
                root, executableName + PlayerPackagedResourceAddresses.DataDirectorySuffix);
            if (Directory.Exists(directDataDirectory) &&
                (File.GetAttributes(directDataDirectory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The Player data directory cannot be a linked directory.");
            var directArchive = GetPlayerResourceArchivePath(directDataDirectory);
            if (File.Exists(directArchive))
                return LoadArchivedPackagedStartup(root, directDataDirectory, directArchive);
            var directMetadata = Path.Combine(
                directDataDirectory, "res", RuntimeMetadataSerializer.FileName);
            if (File.Exists(directMetadata)) return LoadLoosePackagedStartup(root, directMetadata);
        }

        if (!Directory.Exists(root)) return new PlayerStartup(Path.Combine(root, "Game"), root, null);
        var dataDirectories = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).EndsWith(
                PlayerPackagedResourceAddresses.DataDirectorySuffix,
                StringComparison.OrdinalIgnoreCase))
            .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            .ToArray();
        var candidates = dataDirectories
            .Select(CreateMetadataSource)
            .Where(static source => source is not null)
            .Cast<PackagedMetadataSource>()
            .ToArray();
        if (candidates.Length == 0)
            return new PlayerStartup(Path.Combine(root, "Game"), root, null);

        var valid = new List<PlayerStartup>(candidates.Length);
        var failures = new List<Exception>();
        foreach (var candidate in candidates)
        {
            try
            {
                valid.Add(candidate.IsArchive
                    ? LoadArchivedPackagedStartup(root, candidate.DataDirectory, candidate.Path)
                    : LoadLoosePackagedStartup(root, candidate.Path));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                               InvalidDataException)
            {
                failures.Add(exception);
            }
        }
        if (valid.Count == 1) return valid[0];
        if (valid.Count > 1)
            throw new InvalidDataException(
                $"The Player directory contains multiple runtime data folders: '{root}'.");
        throw new InvalidDataException(
            $"No valid Player runtime metadata was found below '{root}'.",
            failures.Count == 1 ? failures[0] : new AggregateException(failures));
    }

    private static PackagedMetadataSource? CreateMetadataSource(string dataDirectory)
    {
        var archive = GetPlayerResourceArchivePath(dataDirectory);
        if (File.Exists(archive)) return new PackagedMetadataSource(dataDirectory, archive, IsArchive: true);
        var metadata = Path.Combine(dataDirectory, "res", RuntimeMetadataSerializer.FileName);
        return File.Exists(metadata)
            ? new PackagedMetadataSource(dataDirectory, metadata, IsArchive: false)
            : null;
    }

    private static string GetPlayerResourceArchivePath(string dataDirectory) => Path.Combine(
        dataDirectory,
        PlayerPackagedResourceAddresses.ResourcesDirectoryName,
        PlayerPackagedResourceAddresses.PlayerArchiveFileName);

    private static PlayerStartup LoadArchivedPackagedStartup(
        string root,
        string dataDirectory,
        string archivePath)
    {
        EnsurePhysicalDirectory(dataDirectory, "data");
        var resourcesDirectory = Path.GetDirectoryName(Path.GetFullPath(archivePath)) ??
                                 throw new InvalidDataException("The Player resource archive path is invalid.");
        EnsurePhysicalDirectory(resourcesDirectory, "resource");
        EnsurePhysicalFile(archivePath, "resource archive");
        var archive = BuiltInResourceArchive.Open(archivePath);
        var metadata = RuntimeMetadataSerializer.Deserialize(
            archive.ReadBytes(PlayerPackagedResourceAddresses.RuntimeMetadata));
        var manifest = metadata.PlayerBootstrap ??
                       throw new InvalidDataException(
                           $"Player resource archive has no bootstrap metadata: '{archivePath}'.");
        var expectedDataDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        var actualDataDirectory = Path.TrimEndingDirectorySeparator(manifest.ResolveDataDirectory(root));
        var actualResourceDirectory = Path.TrimEndingDirectorySeparator(
            manifest.ResolveResourceDirectory(root));
        var actualArchive = Path.GetFullPath(manifest.ResolvePlayerResourceArchive(root));
        if (!actualDataDirectory.Equals(expectedDataDirectory, StringComparison.OrdinalIgnoreCase) ||
            !actualResourceDirectory.Equals(expectedDataDirectory, StringComparison.OrdinalIgnoreCase) ||
            !actualArchive.Equals(Path.GetFullPath(archivePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Player resource archive location does not match its bootstrap data directories.");
        EnsurePhysicalDirectory(manifest.ResolveAssemblyDirectory(root), "assembly");
        return CreatePackagedStartup(root, manifest);
    }

    private static PlayerStartup LoadLoosePackagedStartup(string root, string metadataPath)
    {
        var metadata = RuntimeMetadataSerializer.Load(metadataPath);
        var manifest = metadata.PlayerBootstrap ??
                       throw new InvalidDataException(
                           $"Player runtime metadata has no bootstrap section: '{metadataPath}'.");
        var expectedResourceDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetDirectoryName(Path.GetFullPath(metadataPath))!);
        var expectedDataDirectory = Path.TrimEndingDirectorySeparator(
            Directory.GetParent(expectedResourceDirectory)?.FullName ?? string.Empty);
        var actualDataDirectory = Path.TrimEndingDirectorySeparator(
            manifest.ResolveDataDirectory(root));
        var actualResourceDirectory = Path.TrimEndingDirectorySeparator(
            manifest.ResolveResourceDirectory(root));
        if (!actualDataDirectory.Equals(expectedDataDirectory, StringComparison.OrdinalIgnoreCase) ||
            !actualResourceDirectory.Equals(expectedResourceDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Player runtime metadata location does not match its bootstrap data directories.");
        EnsurePhysicalDirectory(actualDataDirectory, "data");
        EnsurePhysicalDirectory(actualResourceDirectory, "resource");
        EnsurePhysicalDirectory(manifest.ResolveAssemblyDirectory(root), "assembly");
        return CreatePackagedStartup(root, manifest);
    }

    private static void EnsurePhysicalDirectory(string path, string description)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"The packaged Player {description} directory is missing: '{path}'.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                $"The packaged Player {description} directory cannot be a linked directory: '{path}'.");
    }

    private static void EnsurePhysicalFile(string path, string description)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"The packaged Player {description} is missing.", path);
        var file = new FileInfo(path);
        file.Refresh();
        if (file.LinkTarget is not null || (file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                $"The packaged Player {description} cannot be a linked file: '{path}'.");
    }

    private static PlayerStartup CreatePackagedStartup(string root, PlayerBootstrapManifest manifest)
    {
        if (manifest.Version >= 4)
        {
            var archivePath = manifest.ResolvePlayerResourceArchive(root);
            EnsurePhysicalFile(archivePath, "resource archive");
            var archive = BuiltInResourceArchive.Open(archivePath);
            BuildTargetManifestSerializer.SetCurrent(archive.ReadBytes(manifest.BuildTargetResource));
        }
        else
        {
            var targetManifest = manifest.ResolveBuildTargetManifest(root);
            BuildTargetManifestSerializer.SetCurrentPath(targetManifest);
        }
        return new PlayerStartup(
            manifest.ResolveResourceDirectory(root), manifest.ResolveAssemblyDirectory(root), manifest);
    }

    private sealed record PlayerStartup(
        string ProjectPath,
        string NativeLibraryDirectory,
        PlayerBootstrapManifest? Manifest);

    private sealed record PackagedMetadataSource(
        string DataDirectory,
        string Path,
        bool IsArchive);
}

public sealed class PlayerRuntimeSession : IDisposable
{
    private ServiceProvider _services;
    private ISceneRuntimeFactory _runtimeFactory;
    private IRuntimeSceneManager _sceneManager;
    private readonly PlayerAssetBundleBootstrap? _assetBundles;
    private readonly PlayerStagedRuntimeCoordinator? _stagedRuntime;
    private readonly List<SceneRuntime> _runtimes = [];
    private bool _started;
    private bool _isAotStage;
    private bool _transitioning;
    private bool _disposed;

    internal PlayerRuntimeSession(string projectPath, string? persistentDataPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        var fullProjectPath = Path.GetFullPath(projectPath);
        var runtimeWorkspace = ProjectWorkspace.OpenRuntime(fullProjectPath);
        if (runtimeWorkspace.RuntimeMetadata?.PlayerBootstrap is not null)
        {
            _stagedRuntime = PlayerStagedRuntimeCoordinator.Create(fullProjectPath, persistentDataPath);
            Workspace = _stagedRuntime.Workspace;
            var stagedSettings = _stagedRuntime.ProjectSettings;
            Application.companyName = stagedSettings.CompanyName;
            Application.productName = stagedSettings.ProductName;
            Application.isEditor = false;
            Application.isPlaying = false;
            Time.fixedDeltaTime = Fix64.Parse(Workspace.Project.FixedDeltaTime);
            var stage = _stagedRuntime.AotStage;
            _services = stage.Services;
            _runtimeFactory = stage.SceneRuntimeFactory;
            _sceneManager = stage.SceneManager;
            _assetBundles = _stagedRuntime.UpdateBootstrap;
            _isAotStage = true;
            AttachSceneManager();
            foreach (var scene in _sceneManager.LoadedScenes.ToArray()) OnSceneLoaded(scene, LoadSceneMode.Single);
            return;
        }
        _services = new ServiceCollection()
            .AddBEnginePlayer(fullProjectPath, persistentDataPath)
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        try
        {
            Workspace = _services.GetRequiredService<ProjectWorkspace>();
            var projectSettings = _services.GetRequiredService<ProjectSettingsData>();
            Application.companyName = projectSettings.CompanyName;
            Application.productName = projectSettings.ProductName;
            Application.isEditor = false;
            Application.isPlaying = false;
            Time.fixedDeltaTime = Fix64.Parse(Workspace.Project.FixedDeltaTime);
            _runtimeFactory = _services.GetRequiredService<ISceneRuntimeFactory>();
            _sceneManager = _services.GetRequiredService<IRuntimeSceneManager>();
            _assetBundles = _services.GetService<PlayerAssetBundleBootstrap>();
            _assetBundles?.InitializeAsync()
                .ConfigureAwait(false).GetAwaiter().GetResult();
            try
            {
                _services.GetService<PlayerHotUpdateSession>()?.Activate(_services);
                AttachSceneManager();
                _sceneManager.LoadScene(Workspace.StartupScenePath, LoadSceneMode.Single);
                PlayerStartupDiagnostics.Phase("06_SCENE_LOADED",
                    $"scene={Workspace.Project.StartupScene};mode=validation");
            }
            catch (Exception exception)
            {
                _assetBundles?.RollbackAfterStartupFailure(exception);
                throw;
            }
        }
        catch
        {
            _services.Dispose();
            throw;
        }
    }

    public ProjectWorkspace Workspace { get; }
    public IServiceProvider Services => _services;
    public IReadOnlyList<Scene> LoadedScenes => _sceneManager.LoadedScenes;
    public Scene? ActiveScene => _sceneManager.ActiveScene;
    public bool IsStarted => _started;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;
        _started = true;
        Application.isEditor = false;
        Application.isPlaying = true;
        try
        {
            foreach (var runtime in _runtimes.ToArray()) runtime.Start();
            if (_isAotStage)
            {
                PlayerStartupDiagnostics.Phase("03_AOT_SCENE_STARTED", "mode=runtime-session");
                _stagedRuntime!.Flow.Start();
            }
            else
            {
                _assetBundles?.CommitStartup();
                PlayerStartupDiagnostics.Phase("07_GAME_STARTED", "mode=runtime-session");
            }
        }
        catch (Exception exception)
        {
            _started = false;
            Application.isPlaying = false;
            _assetBundles?.RollbackAfterStartupFailure(exception);
            throw;
        }
    }

    public void Tick(TimeSpan elapsed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started) throw new InvalidOperationException("The Player runtime session has not been started.");
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        try
        {
            if (_isAotStage) _stagedRuntime!.Flow.Pump();
            if (!Application.isFocused && !Application.runInBackground) return;
            foreach (var runtime in _runtimes.ToArray()) runtime.Tick((Fix64)elapsed.TotalSeconds);
            if (_isAotStage)
            {
                _stagedRuntime!.Flow.Pump();
                TryEnterGame();
            }
        }
        finally { Input.EndFrame(); }
    }

    public void SetFocused(bool focused) => Application.SetFocus(focused);
    public void SetPaused(bool paused) => Application.SetPaused(paused);
    public void RaiseLowMemory() => Application.RaiseLowMemory();

    public void Stop()
    {
        if (_disposed || !_started) return;
        for (var index = _runtimes.Count - 1; index >= 0; index--) _runtimes[index].Stop();
        _started = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        DetachSceneManager();
        foreach (var scene in _sceneManager.LoadedScenes.ToArray())
            _sceneManager.UnregisterScene(scene, disposeScene: true);
        _runtimes.Clear();
        if (_stagedRuntime is null) _services.Dispose();
        else _stagedRuntime.Dispose();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode _)
    {
        if (_runtimes.Any(runtime => ReferenceEquals(runtime.Scene, scene))) return;
        var runtime = _runtimeFactory.Create(scene);
        _runtimes.Add(runtime);
        if (_started) runtime.Start();
    }

    private void OnSceneUnloaded(Scene scene) =>
        _runtimes.RemoveAll(runtime => ReferenceEquals(runtime.Scene, scene));

    private void TryEnterGame()
    {
        if (_transitioning || !_stagedRuntime!.Flow.EnterGameRequested) return;
        _transitioning = true;
        PlayerApplicationStage next;
        try { next = _stagedRuntime.CreateGameStage(); }
        catch (Exception exception)
        {
            PlayerStartupDiagnostics.Failure("HOTUPDATE_STAGE", exception);
            _stagedRuntime.Flow.ReportTransitionFailure(exception);
            _transitioning = false;
            return;
        }

        try
        {
            for (var index = _runtimes.Count - 1; index >= 0; index--) _runtimes[index].Stop();
            DetachSceneManager();
            foreach (var scene in _sceneManager.LoadedScenes.ToArray())
                _sceneManager.UnregisterScene(scene, disposeScene: true);
            _runtimes.Clear();
            _services = next.Services;
            _runtimeFactory = next.SceneRuntimeFactory;
            _sceneManager = next.SceneManager;
            AttachSceneManager();
            foreach (var scene in _sceneManager.LoadedScenes.ToArray()) OnSceneLoaded(scene, LoadSceneMode.Single);
            foreach (var runtime in _runtimes.ToArray()) runtime.Start();
            _isAotStage = false;
            _assetBundles?.CommitStartup();
            _stagedRuntime.CompleteTransition();
            PlayerStartupDiagnostics.Phase("06_SCENE_LOADED",
                $"scene={next.ScenePath};mode=runtime-session");
            PlayerStartupDiagnostics.Phase("07_GAME_STARTED", "mode=runtime-session");
        }
        catch (Exception exception)
        {
            _assetBundles?.RollbackAfterStartupFailure(exception);
            throw;
        }
        finally { _transitioning = false; }
    }

    private void AttachSceneManager()
    {
        _sceneManager.SceneLoaded += OnSceneLoaded;
        _sceneManager.SceneUnloaded += OnSceneUnloaded;
    }

    private void DetachSceneManager()
    {
        _sceneManager.SceneLoaded -= OnSceneLoaded;
        _sceneManager.SceneUnloaded -= OnSceneUnloaded;
    }
}
