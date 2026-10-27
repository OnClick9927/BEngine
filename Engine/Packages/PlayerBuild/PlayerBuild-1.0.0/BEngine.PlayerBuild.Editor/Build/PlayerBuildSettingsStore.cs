using BEngine.Build;
using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor;

public static class PlayerBuildSettingsStore
{
    public const string FileName = "BuildSettings.yaml";
    public static event Action<string>? settingsChanged;

    private static FileUtility FileSystem =>
        EditorServiceAccess.GetRequiredService<FileUtility>();
    private static PathUtility PathService =>
        EditorServiceAccess.GetRequiredService<PathUtility>();
    private static PlatformUtility PlatformService =>
        EditorServiceAccess.GetRequiredService<PlatformUtility>();

    public static PlayerBuildSettings Load(string projectPath)
    {
        var fileSystem = FileSystem;
        var pathService = PathService;
        var platformService = PlatformService;
        var workspace = ProjectWorkspace.Open(projectPath, fileSystem, pathService);
        var path = pathService.Combine(workspace.ProjectSettingsPath, FileName);
        var settings = fileSystem.FileExists(path)
            ? YamlUtility.Deserialize<PlayerBuildSettings>(fileSystem.ReadAllText(path))
            : CreateDefault(workspace, fileSystem, platformService);
        Normalize(settings, workspace, fileSystem, pathService, platformService);
        return settings;
    }

    public static void Save(string projectPath, PlayerBuildSettings settings)
    {
        SaveCore(projectPath, settings, synchronizeProfile: true);
    }

    internal static void SaveFromProfile(string projectPath, PlayerBuildSettings settings) =>
        SaveCore(projectPath, settings, synchronizeProfile: false);

    private static void SaveCore(
        string projectPath,
        PlayerBuildSettings settings,
        bool synchronizeProfile)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var fileSystem = FileSystem;
        var pathService = PathService;
        var platformService = PlatformService;
        var workspace = ProjectWorkspace.Open(projectPath, fileSystem, pathService);
        Normalize(settings, workspace, fileSystem, pathService, platformService);
        fileSystem.WriteAllText(
            pathService.Combine(workspace.ProjectSettingsPath, FileName),
            YamlUtility.Serialize(settings));
        if (synchronizeProfile)
            PlayerBuildProfileStore.UpdateActiveSettings(projectPath, settings);
        EditorCallbackDispatcher.Invoke(settingsChanged, workspace.RootPath, nameof(settingsChanged));
    }

    public static IReadOnlyList<string> GetEnabledScenes(PlayerBuildSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return [AotProjectLayout.SceneAssetPath];
    }

    public static string ToProjectScenePath(string projectPath, string scenePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenePath);
        var paths = PathService;
        var root = paths.TrimEndingDirectorySeparator(paths.GetFullPath(projectPath));
        var fullPath = paths.IsPathRooted(scenePath)
            ? paths.GetFullPath(scenePath)
            : paths.GetFullPath(paths.Combine(root,
                scenePath.Replace('/', paths.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(root + paths.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Build scene escapes project root: '{scenePath}'.");
        var relative = paths.GetRelativePath(root, fullPath).Replace('\\', '/');
        if (!relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            !relative.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Build scene must be an Assets/*.scene.yaml file: '{scenePath}'.");
        return relative;
    }

    private static PlayerBuildSettings CreateDefault(
        ProjectWorkspace workspace,
        FileUtility fileSystem,
        PlatformUtility platformService)
    {
        var settings = new PlayerBuildSettings
        {
            TargetId = BEngine.Platform.BuildTargetUtility.Current.GetPlatformId(
                BEngine.Platform.BuildTargetUtility.Current.InferCurrentDesktop(platformService).Platform),
            HotUpdateStartupScene = workspace.Project.StartupScene,
            AndroidApplicationIdentifier = CreateDefaultApplicationIdentifier(workspace, fileSystem),
            IosBundleIdentifier = CreateDefaultApplicationIdentifier(workspace, fileSystem)
        };
        if (fileSystem.FileExists(workspace.ProjectSettingsFilePath))
        {
            try
            {
                settings.ScriptingDefineSymbols = YamlUtility.Deserialize<ProjectSettingsData>(
                    fileSystem.ReadAllText(workspace.ProjectSettingsFilePath)).ScriptingDefineSymbols.ToList();
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
            {
                // Build Settings can still be repaired independently from Project Settings.
            }
        }
        settings.Scenes.Add(new PlayerBuildScene { Path = AotProjectLayout.SceneAssetPath });
        return settings;
    }

    private static void Normalize(
        PlayerBuildSettings settings,
        ProjectWorkspace workspace,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService)
    {
        if (!string.Equals(settings.Format, PlayerBuildSettings.CurrentFormat, StringComparison.Ordinal) ||
            settings.Version != PlayerBuildSettings.CurrentVersion)
            throw new InvalidDataException(
                $"Unsupported {FileName} format '{settings.Format}' version {settings.Version}.");

        try
        {
            settings.TargetId = BEngine.Platform.BuildTargetUtility.Current.NormalizePlatformId(settings.TargetId);
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            settings.TargetId = BEngine.Platform.BuildTargetUtility.Current.GetPlatformId(
                BEngine.Platform.BuildTargetUtility.Current.InferCurrentDesktop(platformService).Platform);
        }
        settings.GraphicsBackends ??= [];
        PlayerGarbageCollectionBuildSettings.ValidateWindows(settings.WindowsGarbageCollection, settings.WindowsGcHeapCount);
        var selectedBackends = PlayerGraphicsBuildSettings.Resolve(new PlayerBuildRequest { GraphicsBackends = settings.GraphicsBackends },
            BEngine.Platform.BuildTargetUtility.Current.Get(settings.TargetId));
        if (!settings.CompileShaders && !settings.RuntimeShaderCompilation && selectedBackends.Contains(BEngine.Rendering.Rhi.GraphicsBackend.Vulkan))
            throw new InvalidDataException("Vulkan without a runtime compiler requires Compile Shaders.");
        settings.HostContractPath = settings.HostContractPath?.Trim() ?? string.Empty;
        settings.BuildVersion = settings.BuildVersion?.Trim() ?? string.Empty;
        if (settings.BuildVersion.Length is 0 or > 64 ||
            settings.BuildVersion.Any(character => char.IsControl(character) || character is '/' or '\\' or '|'))
            throw new InvalidDataException("Build version must contain 1-64 safe characters.");
        settings.HotResourceVersion = NormalizeHotResourceVersion(settings.HotResourceVersion);
        if (!Enum.IsDefined(settings.ContentUpdatePolicy))
            settings.ContentUpdatePolicy = PlayerContentUpdatePolicy.FallbackToLastValid;
        if (!Enum.IsDefined(settings.ManagedStripping))
            settings.ManagedStripping = PlayerManagedStrippingLevel.Disabled;
        if (!Enum.IsDefined(settings.WebScriptCompilation))
            settings.WebScriptCompilation = WebScriptCompilationMode.Interpreter;
        settings.CacheDirectory = PlayerBootstrapManifest.NormalizeCacheDirectory(
            settings.CacheDirectory);
        settings.SplashImage = settings.SplashImage?.Trim().Replace('\\', '/') ?? string.Empty;
        if (settings.SplashImage.Equals("Assets/Aot/BEngine.png",
                StringComparison.OrdinalIgnoreCase) &&
            fileSystem.FileExists(workspace.ResolveInside(
                AotProjectLayout.LogoAssetPath.Replace('/', pathService.DirectorySeparatorChar))))
            settings.SplashImage = AotProjectLayout.LogoAssetPath;
        settings.SplashBackgroundColor = settings.SplashBackgroundColor?.Trim().ToUpperInvariant() ?? string.Empty;
        settings.AndroidApplicationIdentifier = NormalizeApplicationIdentifier(
            settings.AndroidApplicationIdentifier, workspace);
        settings.IosBundleIdentifier = NormalizeApplicationIdentifier(
            settings.IosBundleIdentifier, workspace);
        settings.AndroidMinimumApiLevel = NormalizeAndroidMinimumApiLevel(
            settings.AndroidMinimumApiLevel);
        settings.IosMinimumVersion = NormalizeIosMinimumVersion(settings.IosMinimumVersion);
        settings.IosCodesignKey = NormalizeIosSigningValue(
            settings.IosCodesignKey, "iOS signing certificate");
        settings.IosProvisioningProfile = NormalizeIosSigningValue(
            settings.IosProvisioningProfile, "iOS provisioning profile");
        settings.HotUpdateStartupScene = NormalizeStartupScene(
            settings.HotUpdateStartupScene, workspace, fileSystem, pathService);
        settings.ScriptingDefineSymbols = NormalizeScriptingDefineSymbols(
            settings.ScriptingDefineSymbols);
        if (settings.SplashScreenEnabled)
        {
            if (!string.IsNullOrWhiteSpace(settings.SplashImage))
            {
                var imagePath = ToProjectAssetPath(
                    workspace.RootPath, settings.SplashImage, ".png", pathService);
                if (!fileSystem.FileExists(workspace.ResolveInside(imagePath)))
                    throw new FileNotFoundException("Splash screen image was not found.", imagePath);
                settings.SplashImage = imagePath;
            }
            if (!IsHtmlColor(settings.SplashBackgroundColor))
                throw new InvalidDataException("Splash background color must use #RRGGBB or #RRGGBBAA format.");
            if (!float.IsFinite(settings.SplashMinimumDurationSeconds) ||
                settings.SplashMinimumDurationSeconds is < 0 or > 30)
                throw new InvalidDataException("Splash minimum duration must be between 0 and 30 seconds.");
        }

        settings.Scenes =
        [
            new PlayerBuildScene
            {
                Path = AotProjectLayout.SceneAssetPath,
                Enabled = true
            }
        ];
    }

    internal static void Normalize(string projectPath, PlayerBuildSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var workspace = ProjectWorkspace.Open(projectPath, FileSystem, PathService);
        Normalize(settings, workspace, FileSystem, PathService, PlatformService);
    }

    internal static string NormalizeStartupScene(
        string? value,
        ProjectWorkspace workspace,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? workspace.Project.StartupScene : value.Trim();
        var normalized = ToProjectScenePath(workspace.RootPath, candidate);
        if (AotProjectLayout.IsAotAssetPath(normalized))
            throw new InvalidDataException("The hot-update startup scene must be outside Assets/AOT.");
        if (!fileSystem.FileExists(workspace.ResolveInside(
                normalized.Replace('/', pathService.DirectorySeparatorChar))))
            throw new FileNotFoundException("The hot-update startup scene was not found.", normalized);
        return normalized;
    }

    internal static List<string> NormalizeScriptingDefineSymbols(IEnumerable<string>? values)
    {
        var symbols = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values ?? [])
        {
            var symbol = value?.Trim() ?? string.Empty;
            if (symbol.Length == 0) continue;
            if (!(char.IsLetter(symbol[0]) || symbol[0] == '_') ||
                symbol.Skip(1).Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
                throw new InvalidDataException($"Invalid scripting define symbol '{symbol}'.");
            if (!seen.Add(symbol))
                throw new InvalidDataException($"Duplicate scripting define symbol '{symbol}'.");
            symbols.Add(symbol);
        }
        symbols.Sort(StringComparer.Ordinal);
        return symbols;
    }

    internal static string NormalizeHotResourceVersion(string? value)
        => BEngine.AssetBundles.AssetBundleVersionLabel.NormalizeHotResourceVersion(value);

    internal static int CompareHotResourceVersions(string left, string right) =>
        BEngine.AssetBundles.AssetBundleVersionLabel.CompareHotResourceVersions(left, right);

    private static string ToProjectAssetPath(
        string projectPath,
        string assetPath,
        string extension,
        PathUtility pathService)
    {
        var root = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(projectPath));
        var fullPath = pathService.GetFullPath(pathService.Combine(root,
            assetPath.Replace('/', pathService.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(
                root + pathService.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Splash image escapes project root: '{assetPath}'.");
        var relative = pathService.GetRelativePath(root, fullPath).Replace('\\', '/');
        if (!relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            !pathService.GetExtension(relative).Equals(extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Splash image must be an Assets/*.png file.");
        return relative;
    }

    private static bool IsHtmlColor(string value) =>
        value is { Length: 7 or 9 } && value[0] == '#' &&
        value.AsSpan(1).ToString().All(static character => char.IsAsciiHexDigit(character));

    private static string NormalizeApplicationIdentifier(
        string? value,
        ProjectWorkspace workspace)
    {
        var candidate = string.IsNullOrWhiteSpace(value)
            ? CreateDefaultApplicationIdentifier(workspace, FileSystem)
            : value.Trim().ToLowerInvariant();
        return NormalizeApplicationIdentifier(candidate);
    }

    internal static string NormalizeApplicationIdentifier(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length is < 3 or > 255)
            throw new InvalidDataException("Application identifier must contain 3-255 characters.");
        var segments = normalized.Split('.');
        if (segments.Length < 2 || segments.Any(segment =>
                segment.Length == 0 || !char.IsAsciiLetter(segment[0]) ||
                segment.Skip(1).Any(character => !char.IsAsciiLetterOrDigit(character))))
            throw new InvalidDataException(
                "Application identifier must use reverse-DNS segments containing only ASCII letters and digits.");
        return normalized;
    }

    internal static int NormalizeAndroidMinimumApiLevel(int value)
    {
        if (value is < 21 or > 100)
            throw new InvalidDataException("Android minimum API level must be between 21 and 100.");
        return value;
    }

    internal static string CreateDefaultApplicationIdentifier(ProjectWorkspace workspace)
        => CreateDefaultApplicationIdentifier(workspace, FileSystem);

    private static string CreateDefaultApplicationIdentifier(
        ProjectWorkspace workspace,
        FileUtility fileSystem)
    {
        var company = "bengine";
        if (fileSystem.FileExists(workspace.ProjectSettingsFilePath))
        {
            try
            {
                company = YamlUtility.Deserialize<ProjectSettingsData>(
                    fileSystem.ReadAllText(workspace.ProjectSettingsFilePath)).CompanyName;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
            {
                // A valid Build Settings document remains loadable while Project Settings is repaired.
            }
        }
        return $"com.{IdentifierSegment(company)}.{IdentifierSegment(workspace.Project.Name)}";
    }

    private static string IdentifierSegment(string? source)
    {
        var segment = new string((source ?? string.Empty).Where(char.IsAsciiLetterOrDigit).ToArray())
            .ToLowerInvariant();
        if (segment.Length == 0) return "game";
        return char.IsAsciiLetter(segment[0]) ? segment : "p" + segment;
    }

    internal static string NormalizeIosMinimumVersion(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!Version.TryParse(normalized, out var version) || version.Major is < 12 or > 99 ||
            version.Minor < 0 || version.Build > 999 || version.Revision > 999)
            throw new InvalidDataException(
                "iOS minimum version must be a valid version from 12.0 through 99.x.");
        return normalized;
    }

    internal static string NormalizeIosSigningValue(string? value, string settingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingName);
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > 512 || normalized.Any(char.IsControl) || normalized.Contains(';'))
            throw new InvalidDataException(
                $"{settingName} must contain at most 512 characters and cannot contain control characters or ';'.");
        return normalized;
    }
}
