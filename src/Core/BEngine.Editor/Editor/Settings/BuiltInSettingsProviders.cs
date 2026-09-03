using BEngine.Rendering.Rhi;
using BEngine.Build;
using BEngine.Editor.Documents;

namespace BEngine.Editor;

internal static class BuiltInSettingsProviders
{
    private static readonly string[] Locales = ["中文 (简体)", "English"];
    private static readonly string[] LocaleValues = ["zh-CN", "en-US"];
    private static readonly string[] Fonts = ["BEngine Built-in", "Segoe UI", "Microsoft YaHei"];
    private static readonly string[] Backends = Enum.GetNames<GraphicsBackend>();
    private static string _scriptingSettingsPath = string.Empty;
    private static string _scriptingDefineSymbols = string.Empty;
    private static string _scriptingError = string.Empty;
    private static bool _scriptingDirty;
    private static string _playerBuildProjectPath = string.Empty;
    private static PlayerBuildSettings? _playerBuildSettings;
    private static string _playerBuildError = string.Empty;

    [SettingsProvider]
    private static SettingsProvider GeneralPreferences() => new("Preferences/General", SettingsScope.User,
        ["scale", "font", "language", "locale", "缩放", "字体", "语言"])
    {
        label = EditorLocalization.Tr("General"),
        guiHandler = _ => DrawGeneralPreferences(),
        footerBarGuiHandler = () =>
        {
            GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
            if (GUILayout.Button(EditorLocalization.Tr("Reset"), GUILayout.Width(120)))
                EditorPreferences.ResetToDefaults();
            GUILayout.EndHorizontal();
        }
    };

    [SettingsProvider]
    private static SettingsProvider ThemePreferences() => new("Preferences/Theme", SettingsScope.User,
        ["theme", "skin", "GUISkin", "Light", "Dark", "Classic", "主题", "皮肤"])
    {
        label = EditorLocalization.Tr("Theme"),
        guiHandler = _ =>
        {
            if (!EditorSkinPreferences.Draw(EditorPreferences.current)) return;
            EditorPreferences.Save();
        }
    };

    [SettingsProvider]
    private static SettingsProvider ExternalToolsPreferences() => new("Preferences/External Tools",
        SettingsScope.User, ["script", "code", "IDE", "脚本", "代码"])
    {
        label = EditorLocalization.Tr("External Tools"),
        guiHandler = _ =>
        {
            var preferences = EditorPreferences.current;
            var value = EditorGUILayout.TextField(EditorLocalization.Tr("External Script Editor"),
                preferences.ExternalScriptEditor);
            if (value == preferences.ExternalScriptEditor) return;
            preferences.ExternalScriptEditor = value;
            EditorPreferences.Save();
        }
    };

    [SettingsProvider]
    private static SettingsProvider PlayerProjectSettings() => new("Project/Player", SettingsScope.Project,
        ["company", "product", "resolution", "platform", "build", "splash", "hot update",
            "cache", "公司", "产品", "分辨率", "平台", "构建", "热更新"])
    {
        label = EditorLocalization.Tr("Player"),
        guiHandler = _ => DrawPlayerSettings()
    };

    [SettingsProvider]
    private static SettingsProvider GraphicsProjectSettings() => new("Project/Graphics", SettingsScope.Project,
        ["Vulkan", "OpenGL", "Direct3D", "WebGPU", "图形", "渲染"])
    {
        label = EditorLocalization.Tr("Graphics"),
        guiHandler = _ => DrawGraphicsSettings()
    };

    [SettingsProvider]
    private static SettingsProvider ScriptingProjectSettings() => new("Project/Scripting", SettingsScope.Project,
        ["assembly", "define", "symbol", "script", "程序集", "宏", "脚本"])
    {
        label = EditorLocalization.Tr("Scripting"),
        guiHandler = _ => DrawScriptingSettings()
    };

    private static void DrawGeneralPreferences()
    {
        var value = EditorPreferences.current;
        var changed = false;
        var locale = Math.Max(0, Array.IndexOf(LocaleValues, value.Locale));
        var nextLocale = EditorGUILayout.Popup(EditorLocalization.Tr("Language"), locale, Locales);
        if (nextLocale != locale) { value.Locale = LocaleValues[nextLocale]; changed = true; }
        var scale = EditorGUILayout.Slider(EditorLocalization.Tr("Editor Scale"), value.EditorScale,
            EditorAppearance.MinimumScale, EditorAppearance.MaximumScale);
        if (Math.Abs(scale - value.EditorScale) > .001f) { value.EditorScale = scale; changed = true; }
        var font = Math.Max(0, Array.IndexOf(Fonts, value.EditorFont));
        var nextFont = EditorGUILayout.Popup(EditorLocalization.Tr("Font"), font, Fonts);
        if (nextFont != font) { value.EditorFont = Fonts[nextFont]; changed = true; }
        EditorGUI.BeginDisabledGroup(true);
        _ = EditorGUILayout.IntField(EditorLocalization.Tr("Font Size"), EditorAppearance.DefaultFontSize);
        EditorGUI.EndDisabledGroup();
        var refresh = EditorGUILayout.Toggle(EditorLocalization.Tr("Auto Refresh Assets"), value.AutoRefreshAssets);
        if (refresh != value.AutoRefreshAssets) { value.AutoRefreshAssets = refresh; changed = true; }
        var meta = EditorGUILayout.Toggle(EditorLocalization.Tr("Show Meta Files"), value.ShowAssetMetaFiles);
        if (meta != value.ShowAssetMetaFiles) { value.ShowAssetMetaFiles = meta; changed = true; }
        EditorGUILayout.HelpBox(EditorLocalization.Tr("Built-in font"), MessageType.Info);
        if (changed) EditorPreferences.Save();
    }

    private static void DrawPlayerSettings()
    {
        var value = EditorProjectSettings.current;
        var projectChanged = false;
        var buildChanged = false;
        var build = GetPlayerBuildSettings();

        GUILayout.Label("Identification", EditorStyles.boldLabel);
        var company = EditorGUILayout.TextField(EditorLocalization.Tr("Company Name"), value.CompanyName);
        if (company != value.CompanyName && !string.IsNullOrWhiteSpace(company))
        {
            value.CompanyName = company;
            projectChanged = true;
        }
        var product = EditorGUILayout.TextField(EditorLocalization.Tr("Product Name"), value.ProductName);
        if (product != value.ProductName && !string.IsNullOrWhiteSpace(product))
        {
            value.ProductName = product;
            projectChanged = true;
        }
        var buildVersion = EditorGUILayout.TextField("Version", build.BuildVersion);
        if (!buildVersion.Equals(build.BuildVersion, StringComparison.Ordinal))
        {
            build.BuildVersion = buildVersion;
            buildChanged = true;
        }

        GUILayout.Space(10);
        GUILayout.Label("Build Platform", EditorStyles.boldLabel);
        var platforms = BuildTargetCatalog.Platforms;
        var platformIndex = Math.Max(0, platforms.ToList().FindIndex(platform =>
            platform.PlatformId.Equals(build.TargetId, StringComparison.OrdinalIgnoreCase)));
        var platformLabels = platforms.Select(platform => PlatformLabel(platform.Platform)).ToArray();
        var nextPlatformIndex = EditorGUILayout.Popup("Platform", platformIndex, platformLabels);
        if (nextPlatformIndex != platformIndex)
        {
            build.TargetId = platforms[nextPlatformIndex].PlatformId;
            buildChanged = true;
        }
        var selectedPlatform = BuildTargetCatalog.GetPlatform(build.TargetId).Platform;

        GUILayout.Space(7);
        projectChanged |= DrawPlatformPlayerSettings(
            value, build, selectedPlatform, out var platformBuildChanged);
        buildChanged |= platformBuildChanged;

        GUILayout.Space(10);
        GUILayout.Label("Build", EditorStyles.boldLabel);
        var development = EditorGUILayout.Toggle("Development Build", build.DevelopmentBuild);
        if (development != build.DevelopmentBuild)
        {
            build.DevelopmentBuild = development;
            buildChanged = true;
        }
        var debugSymbols = EditorGUILayout.Toggle("Debug Symbols", build.IncludeDebugSymbols);
        if (debugSymbols != build.IncludeDebugSymbols)
        {
            build.IncludeDebugSymbols = debugSymbols;
            buildChanged = true;
        }
        if (selectedPlatform is BuildTargetPlatform.Windows or BuildTargetPlatform.Linux or
            BuildTargetPlatform.MacOS)
        {
            var selfContained = EditorGUILayout.Toggle("Self Contained Player", build.SelfContained);
            if (selfContained != build.SelfContained)
            {
                build.SelfContained = selfContained;
                buildChanged = true;
            }
        }
        else if (!build.SelfContained)
        {
            build.SelfContained = true;
            buildChanged = true;
        }
        var managedStripping = (PlayerManagedStrippingLevel)EditorGUILayout.EnumPopup(
            "Managed Stripping", build.ManagedStripping);
        if (managedStripping != build.ManagedStripping)
        {
            build.ManagedStripping = managedStripping;
            buildChanged = true;
        }

        GUILayout.Space(10);
        GUILayout.Label("Splash Screen", EditorStyles.boldLabel);
        var splashEnabled = EditorGUILayout.Toggle("Show Splash Screen", build.SplashScreenEnabled);
        if (splashEnabled != build.SplashScreenEnabled)
        {
            build.SplashScreenEnabled = splashEnabled;
            buildChanged = true;
        }
        using (new EditorGUI.DisabledScope(!splashEnabled))
        {
            var splashImage = EditorGUILayout.TextField("Image (Assets PNG)", build.SplashImage);
            if (!splashImage.Equals(build.SplashImage, StringComparison.Ordinal))
            {
                build.SplashImage = splashImage;
                buildChanged = true;
            }
            var splashColor = EditorGUILayout.TextField("Background", build.SplashBackgroundColor);
            if (!splashColor.Equals(build.SplashBackgroundColor, StringComparison.Ordinal))
            {
                build.SplashBackgroundColor = splashColor;
                buildChanged = true;
            }
            var splashDuration = EditorGUILayout.FloatField(
                "Minimum Seconds", build.SplashMinimumDurationSeconds);
            if (!splashDuration.Equals(build.SplashMinimumDurationSeconds))
            {
                build.SplashMinimumDurationSeconds = splashDuration;
                buildChanged = true;
            }
        }

        GUILayout.Space(10);
        GUILayout.Label("Content Delivery", EditorStyles.boldLabel);
        var hotUpdate = EditorGUILayout.Toggle("C# Hot Update", build.EnableHotUpdate);
        if (hotUpdate != build.EnableHotUpdate)
        {
            build.EnableHotUpdate = hotUpdate;
            buildChanged = true;
        }
        var hotResourceVersion = EditorGUILayout.TextField(
            "Hot Resource Version", build.HotResourceVersion);
        if (!hotResourceVersion.Equals(build.HotResourceVersion, StringComparison.Ordinal))
        {
            build.HotResourceVersion = hotResourceVersion;
            buildChanged = true;
        }
        var updatePolicy = (PlayerContentUpdatePolicy)EditorGUILayout.EnumPopup(
            "Startup Update", build.ContentUpdatePolicy);
        if (updatePolicy != build.ContentUpdatePolicy)
        {
            build.ContentUpdatePolicy = updatePolicy;
            buildChanged = true;
        }
        var compressBundles = EditorGUILayout.Toggle("Compress AssetBundles",
            build.CompressAssetBundles);
        if (compressBundles != build.CompressAssetBundles)
        {
            build.CompressAssetBundles = compressBundles;
            buildChanged = true;
        }
        var cacheDirectory = EditorGUILayout.TextField("Player Cache (Relative)", build.CacheDirectory);
        if (!cacheDirectory.Equals(build.CacheDirectory, StringComparison.Ordinal))
        {
            build.CacheDirectory = cacheDirectory;
            buildChanged = true;
        }

        GUILayout.Space(10);
        GUILayout.Label("Diagnostics", EditorStyles.boldLabel);
        var writePlayerLog = EditorGUILayout.Toggle("Write Player Log", build.WritePlayerLog);
        if (writePlayerLog != build.WritePlayerLog)
        {
            build.WritePlayerLog = writePlayerLog;
            buildChanged = true;
        }

        if (projectChanged) EditorProjectSettings.Save();
        if (buildChanged) SavePlayerBuildSettings(build);
        if (_playerBuildError.Length > 0)
        {
            GUILayout.Space(8);
            EditorGUILayout.HelpBox(_playerBuildError, MessageType.Error);
        }
    }

    private static bool DrawPlatformPlayerSettings(
        BEngine.ProjectSystem.ProjectSettingsData value,
        PlayerBuildSettings build,
        BuildTargetPlatform platform,
        out bool buildChanged)
    {
        var changed = false;
        buildChanged = false;
        if (platform is BuildTargetPlatform.Windows or BuildTargetPlatform.Linux or
            BuildTargetPlatform.MacOS)
        {
            GUILayout.Label("Desktop Display", EditorStyles.boldLabel);
            var width = Math.Max(320, EditorGUILayout.IntField(
                EditorLocalization.Tr("Default Width"), value.DefaultScreenWidth));
            if (width != value.DefaultScreenWidth)
            {
                value.DefaultScreenWidth = width;
                changed = true;
            }
            var height = Math.Max(200, EditorGUILayout.IntField(
                EditorLocalization.Tr("Default Height"), value.DefaultScreenHeight));
            if (height != value.DefaultScreenHeight)
            {
                value.DefaultScreenHeight = height;
                changed = true;
            }
            var fullscreen = EditorGUILayout.Toggle(
                EditorLocalization.Tr("Full Screen"), value.FullScreen);
            if (fullscreen != value.FullScreen)
            {
                value.FullScreen = fullscreen;
                changed = true;
            }
        }
        else if (platform == BuildTargetPlatform.Web)
        {
            GUILayout.Label("Web Canvas", EditorStyles.boldLabel);
            var width = Math.Max(320, EditorGUILayout.IntField("Canvas Width",
                value.DefaultScreenWidth));
            if (width != value.DefaultScreenWidth)
            {
                value.DefaultScreenWidth = width;
                changed = true;
            }
            var height = Math.Max(200, EditorGUILayout.IntField("Canvas Height",
                value.DefaultScreenHeight));
            if (height != value.DefaultScreenHeight)
            {
                value.DefaultScreenHeight = height;
                changed = true;
            }
        }
        else
        {
            if (platform == BuildTargetPlatform.Android)
            {
                GUILayout.Label("Android", EditorStyles.boldLabel);
                var identifier = EditorGUILayout.TextField(
                    "Application Identifier", build.AndroidApplicationIdentifier);
                if (!identifier.Equals(build.AndroidApplicationIdentifier,
                        StringComparison.Ordinal))
                {
                    build.AndroidApplicationIdentifier = identifier;
                    buildChanged = true;
                }
                var minimumApi = EditorGUILayout.IntField(
                    "Minimum API Level", build.AndroidMinimumApiLevel);
                if (minimumApi != build.AndroidMinimumApiLevel)
                {
                    build.AndroidMinimumApiLevel = minimumApi;
                    buildChanged = true;
                }
                var appBundle = EditorGUILayout.Toggle(
                    "Build App Bundle", build.AndroidBuildAppBundle);
                if (appBundle != build.AndroidBuildAppBundle)
                {
                    build.AndroidBuildAppBundle = appBundle;
                    buildChanged = true;
                }
            }
            else
            {
                GUILayout.Label("iOS", EditorStyles.boldLabel);
                var identifier = EditorGUILayout.TextField(
                    "Bundle Identifier", build.IosBundleIdentifier);
                if (!identifier.Equals(build.IosBundleIdentifier, StringComparison.Ordinal))
                {
                    build.IosBundleIdentifier = identifier;
                    buildChanged = true;
                }
                var minimumVersion = EditorGUILayout.TextField(
                    "Minimum iOS Version", build.IosMinimumVersion);
                if (!minimumVersion.Equals(build.IosMinimumVersion, StringComparison.Ordinal))
                {
                    build.IosMinimumVersion = minimumVersion;
                    buildChanged = true;
                }
            }
        }
        return changed;
    }

    private static PlayerBuildSettings GetPlayerBuildSettings()
    {
        var projectPath = EditorApplication.projectPath;
        if (_playerBuildSettings is not null && _playerBuildProjectPath.Equals(
                projectPath, StringComparison.OrdinalIgnoreCase))
            return _playerBuildSettings;

        _playerBuildProjectPath = projectPath;
        _playerBuildError = string.Empty;
        if (string.IsNullOrWhiteSpace(projectPath))
            return _playerBuildSettings = new PlayerBuildSettings
            {
                TargetId = BuildTargetCatalog.InferCurrentDesktopPlatformId()
            };
        try
        {
            return _playerBuildSettings = PlayerBuildSettingsStore.Load(projectPath);
        }
        catch (Exception exception)
        {
            _playerBuildError = exception.Message;
            return _playerBuildSettings = new PlayerBuildSettings
            {
                TargetId = BuildTargetCatalog.InferCurrentDesktopPlatformId()
            };
        }
    }

    private static void SavePlayerBuildSettings(PlayerBuildSettings settings)
    {
        if (string.IsNullOrWhiteSpace(_playerBuildProjectPath)) return;
        try
        {
            PlayerBuildSettingsStore.Save(_playerBuildProjectPath, settings);
            _playerBuildError = string.Empty;
        }
        catch (Exception exception)
        {
            _playerBuildError = exception.Message;
        }
    }

    private static string PlatformLabel(BuildTargetPlatform platform) => platform switch
    {
        BuildTargetPlatform.MacOS => "macOS",
        BuildTargetPlatform.IOS => "iOS",
        _ => platform.ToString()
    };

    private static void DrawGraphicsSettings()
    {
        var value = EditorProjectSettings.current;
        var backend = Math.Max(0, Array.FindIndex(Backends, item => item.Equals(value.GraphicsBackend,
            StringComparison.OrdinalIgnoreCase)));
        var next = EditorGUILayout.Popup(EditorLocalization.Tr("Graphics Backend"), backend, Backends);
        EditorGUILayout.HelpBox(EditorLocalization.Tr("Restart renderer"), MessageType.Info);
        if (next == backend) return;
        value.GraphicsBackend = Backends[next];
        EditorProjectSettings.Save();
    }

    private static void DrawScriptingSettings()
    {
        if (!_scriptingSettingsPath.Equals(EditorProjectSettings.settingsPath, StringComparison.OrdinalIgnoreCase))
        {
            _scriptingSettingsPath = EditorProjectSettings.settingsPath;
            _scriptingDefineSymbols = string.Join(';', EditorProjectSettings.current.ScriptingDefineSymbols);
            _scriptingDirty = false;
            _scriptingError = string.Empty;
        }

        var next = EditorGUILayout.TextField("Scripting Define Symbols", _scriptingDefineSymbols);
        if (!next.Equals(_scriptingDefineSymbols, StringComparison.Ordinal))
        {
            _scriptingDefineSymbols = next;
            _scriptingDirty = true;
            _scriptingError = ValidateScriptingSymbols(ParseScriptingSymbols(next));
        }
        if (_scriptingError.Length > 0) EditorGUILayout.HelpBox(_scriptingError, MessageType.Error);
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUI.BeginDisabledGroup(!_scriptingDirty);
        if (GUILayout.Button("Revert", GUILayout.Width(80)))
        {
            _scriptingDefineSymbols = string.Join(';', EditorProjectSettings.current.ScriptingDefineSymbols);
            _scriptingDirty = false;
            _scriptingError = string.Empty;
        }
        EditorGUI.EndDisabledGroup();
        EditorGUI.BeginDisabledGroup(!_scriptingDirty || _scriptingError.Length > 0);
        if (GUILayout.Button("Apply", GUILayout.Width(80)))
        {
            EditorProjectSettings.current.ScriptingDefineSymbols = ParseScriptingSymbols(_scriptingDefineSymbols);
            EditorProjectSettings.Save();
            _scriptingDirty = false;
            EditorApplication.delayCall += () => CompilationPipeline.RequestScriptCompilation();
        }
        EditorGUI.EndDisabledGroup();
        GUILayout.EndHorizontal();
    }

    private static List<string> ParseScriptingSymbols(string value) => value
        .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    private static string ValidateScriptingSymbols(IEnumerable<string> symbols)
    {
        foreach (var symbol in symbols)
        {
            if (!(char.IsLetter(symbol[0]) || symbol[0] == '_') ||
                symbol.Skip(1).Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
                return $"Invalid scripting define symbol '{symbol}'.";
        }
        return string.Empty;
    }
}
