using BEngine.Build;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor;

public static class PlayerBuildSettingsStore
{
    public const string FileName = "BuildSettings.yaml";
    public static event Action<string>? settingsChanged;

    public static PlayerBuildSettings Load(string projectPath)
    {
        var workspace = ProjectWorkspace.Open(projectPath);
        var path = Path.Combine(workspace.ProjectSettingsPath, FileName);
        var settings = File.Exists(path)
            ? YamlUtility.Load<PlayerBuildSettings>(path)
            : CreateDefault(workspace);
        Normalize(settings, workspace);
        return settings;
    }

    public static void Save(string projectPath, PlayerBuildSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var workspace = ProjectWorkspace.Open(projectPath);
        Normalize(settings, workspace);
        YamlUtility.Save(settings, Path.Combine(workspace.ProjectSettingsPath, FileName));
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
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
        var fullPath = Path.IsPathRooted(scenePath)
            ? Path.GetFullPath(scenePath)
            : Path.GetFullPath(Path.Combine(root,
                scenePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Build scene escapes project root: '{scenePath}'.");
        var relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        if (!relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            !relative.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Build scene must be an Assets/*.scene.yaml file: '{scenePath}'.");
        return relative;
    }

    private static PlayerBuildSettings CreateDefault(ProjectWorkspace workspace)
    {
        var settings = new PlayerBuildSettings
        {
            TargetId = BuildTargetCatalog.InferCurrentDesktopPlatformId(),
            AndroidApplicationIdentifier = CreateDefaultApplicationIdentifier(workspace),
            IosBundleIdentifier = CreateDefaultApplicationIdentifier(workspace)
        };
        settings.Scenes.Add(new PlayerBuildScene { Path = AotProjectLayout.SceneAssetPath });
        return settings;
    }

    private static void Normalize(PlayerBuildSettings settings, ProjectWorkspace workspace)
    {
        if (!string.Equals(settings.Format, PlayerBuildSettings.CurrentFormat, StringComparison.Ordinal) ||
            settings.Version != PlayerBuildSettings.CurrentVersion)
            throw new InvalidDataException(
                $"Unsupported {FileName} format '{settings.Format}' version {settings.Version}.");

        try
        {
            settings.TargetId = BuildTargetCatalog.NormalizePlatformId(settings.TargetId);
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            settings.TargetId = BuildTargetCatalog.InferCurrentDesktopPlatformId();
        }
        settings.BuildVersion = settings.BuildVersion?.Trim() ?? string.Empty;
        if (settings.BuildVersion.Length is 0 or > 64 ||
            settings.BuildVersion.Any(character => char.IsControl(character) || character is '/' or '\\' or '|'))
            throw new InvalidDataException("Build version must contain 1-64 safe characters.");
        settings.HotResourceVersion = NormalizeHotResourceVersion(settings.HotResourceVersion);
        if (!Enum.IsDefined(settings.ContentUpdatePolicy))
            settings.ContentUpdatePolicy = PlayerContentUpdatePolicy.FallbackToLastValid;
        if (!Enum.IsDefined(settings.ManagedStripping))
            settings.ManagedStripping = PlayerManagedStrippingLevel.Disabled;
        settings.CacheDirectory = PlayerBootstrapManifest.NormalizeCacheDirectory(
            settings.CacheDirectory);
        settings.SplashImage = settings.SplashImage?.Trim().Replace('\\', '/') ?? string.Empty;
        settings.SplashBackgroundColor = settings.SplashBackgroundColor?.Trim().ToUpperInvariant() ?? string.Empty;
        settings.AndroidApplicationIdentifier = NormalizeApplicationIdentifier(
            settings.AndroidApplicationIdentifier, workspace);
        settings.IosBundleIdentifier = NormalizeApplicationIdentifier(
            settings.IosBundleIdentifier, workspace);
        settings.AndroidMinimumApiLevel = NormalizeAndroidMinimumApiLevel(
            settings.AndroidMinimumApiLevel);
        settings.IosMinimumVersion = NormalizeIosMinimumVersion(settings.IosMinimumVersion);
        if (settings.SplashScreenEnabled)
        {
            if (!string.IsNullOrWhiteSpace(settings.SplashImage))
            {
                var imagePath = ToProjectAssetPath(workspace.RootPath, settings.SplashImage, ".png");
                if (!File.Exists(workspace.ResolveInside(imagePath)))
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

    internal static string NormalizeHotResourceVersion(string? value)
        => BEngine.AssetBundles.AssetBundleVersionLabel.NormalizeHotResourceVersion(value);

    internal static int CompareHotResourceVersions(string left, string right) =>
        BEngine.AssetBundles.AssetBundleVersionLabel.CompareHotResourceVersions(left, right);

    private static string ToProjectAssetPath(string projectPath, string assetPath, string extension)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
        var fullPath = Path.GetFullPath(Path.Combine(root,
            assetPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Splash image escapes project root: '{assetPath}'.");
        var relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        if (!relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetExtension(relative).Equals(extension, StringComparison.OrdinalIgnoreCase))
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
            ? CreateDefaultApplicationIdentifier(workspace)
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

    private static string CreateDefaultApplicationIdentifier(ProjectWorkspace workspace)
    {
        var company = "bengine";
        if (File.Exists(workspace.ProjectSettingsFilePath))
        {
            try
            {
                company = YamlUtility.Load<ProjectSettingsData>(workspace.ProjectSettingsFilePath).CompanyName;
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
}
