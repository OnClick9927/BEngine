using BEngine.Build;
using BEngine.Rendering.Rhi;
using BEngine.Editor.Documents;
namespace BEngine.Editor;
internal static class PlayerSettingsProvider
{
    private static string _playerBuildProjectPath = string.Empty;
    private static PlayerBuildSettings? _playerBuildSettings;
    private static string _playerBuildError = string.Empty;
    [SettingsProvider]
    private static SettingsProvider PlayerProjectSettings() => new("Project/Player", SettingsScope.Project,
        ["company", "product", "resolution", "platform", "build", "splash", "hot update",
            "cache", "公司", "产品", "分辨率", "平台", "构建", "热更新"])
    {
        label = EditorLocalization.Tr("Player"),
        guiHandler = _ => DrawPlayerSettings()
    };

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
        var platforms = BEngine.Platform.BuildTargetUtility.Current.Platforms;
        var platformIndex = Math.Max(0, platforms.ToList().FindIndex(platform =>
            platform.PlatformId.Equals(build.TargetId, StringComparison.OrdinalIgnoreCase)));
        var nextPlatformIndex = DrawPlatformTabs(platforms, platformIndex);
        if (nextPlatformIndex != platformIndex)
        {
            build.TargetId = platforms[nextPlatformIndex].PlatformId;
            build.GraphicsBackends = [];
            build.HostContractPath = string.Empty;
            buildChanged = true;
        }
        var selectedPlatform = BEngine.Platform.BuildTargetUtility.Current.GetPlatform(build.TargetId).Platform;

        GUILayout.Space(7);
        projectChanged |= DrawPlatformPlayerSettings(
            value, build, selectedPlatform, out var platformBuildChanged);
        buildChanged |= platformBuildChanged;

        GUILayout.Space(10);
        GUILayout.Label("Build", EditorStyles.boldLabel);
        var encryptContent = EditorGUILayout.Toggle("Encrypt Code And Resources", build.EncryptContent);
        if (encryptContent != build.EncryptContent)
        {
            build.EncryptContent = encryptContent;
            buildChanged = true;
        }
        if (build.EncryptContent)
            GUILayout.Label("Enter the Encryption Key when building. Use the same key for this Player and its Hot Resources. Keys are not saved in project settings.", EditorStyles.wordWrappedLabel);
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
        if (selectedPlatform == BuildTargetPlatform.Windows)
        {
            var gcMode = (PlayerGarbageCollectionMode)EditorGUILayout.EnumPopup(
                "Garbage Collection", build.WindowsGarbageCollection);
            if (gcMode != build.WindowsGarbageCollection)
            {
                build.WindowsGarbageCollection = gcMode;
                if (gcMode != PlayerGarbageCollectionMode.Server) build.WindowsGcHeapCount = 0;
                buildChanged = true;
            }
            if (gcMode == PlayerGarbageCollectionMode.Server)
            {
                var heaps = Math.Max(0, EditorGUILayout.IntField("GC Heap Count (0 = Runtime Default)", build.WindowsGcHeapCount));
                if (heaps != build.WindowsGcHeapCount)
                {
                    build.WindowsGcHeapCount = heaps;
                    buildChanged = true;
                }
            }
        }
        if (selectedPlatform == BuildTargetPlatform.Web)
        {
            var scriptCompilation = (WebScriptCompilationMode)EditorGUILayout.EnumPopup(
                EditorLocalization.Tr("Web Script Compilation"), build.WebScriptCompilation);
            if (scriptCompilation != build.WebScriptCompilation)
            {
                build.WebScriptCompilation = scriptCompilation;
                buildChanged = true;
            }
            if (build.WebScriptCompilation == WebScriptCompilationMode.AheadOfTime)
                GUILayout.Label(EditorLocalization.Tr("Web Script Precompilation Notice"),
                    EditorStyles.wordWrappedLabel);
        }
        var managedStripping = (PlayerManagedStrippingLevel)EditorGUILayout.EnumPopup(
            "Managed Stripping", build.ManagedStripping);
        if (managedStripping != build.ManagedStripping)
        {
            build.ManagedStripping = managedStripping;
            buildChanged = true;
        }
        if (build.ManagedStripping == PlayerManagedStrippingLevel.Full)
            GUILayout.Label("Full strips the Host, AOT code and unused startup resources. Declare dynamic roots in ProjectSettings/PlayerStripping.yaml or link.xml.", EditorStyles.wordWrappedLabel);

        GUILayout.Space(10);
        GUILayout.Label("Graphics Build", EditorStyles.boldLabel);
        var supportedBackends = PlayerGraphicsBuildSettings.GetSupported(BEngine.Platform.BuildTargetUtility.Current.Get(build.TargetId));
        foreach (var backend in supportedBackends)
        {
            var selected = build.GraphicsBackends.Count == 0 || build.GraphicsBackends.Contains(backend);
            var enabled = EditorGUILayout.Toggle(backend.ToString(), selected);
            if (enabled == selected) continue;
            var selection = build.GraphicsBackends.Count == 0 ? supportedBackends.ToList() : build.GraphicsBackends.ToList();
            if (enabled) selection.Add(backend);
            else if (selection.Count > 1) selection.Remove(backend);
            build.GraphicsBackends = selection;
            buildChanged = true;
        }
        var compileShaders = EditorGUILayout.Toggle("Compile Shaders Offline", build.CompileShaders);
        var runtimeCompiler = EditorGUILayout.Toggle("Runtime Shader Compilation", build.RuntimeShaderCompilation);
        var hostContract = EditorGUILayout.TextField("Hot Update Host Contract", build.HostContractPath);
        if (compileShaders != build.CompileShaders || runtimeCompiler != build.RuntimeShaderCompilation || hostContract != build.HostContractPath)
        {
            build.CompileShaders = compileShaders; build.RuntimeShaderCompilation = runtimeCompiler;
            build.HostContractPath = hostContract; buildChanged = true;
        }
        GUILayout.Label("Vulkan offline artifacts are built into main and hot resources. Disable runtime compilation only when all generated shaders have a build provider. Select the main build's Host contract to check future hot-update APIs.", EditorStyles.wordWrappedLabel);
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
        var startupScene = EditorGUILayout.TextField(
            "Startup Scene", build.HotUpdateStartupScene);
        if (!startupScene.Equals(build.HotUpdateStartupScene, StringComparison.Ordinal))
        {
            build.HotUpdateStartupScene = startupScene;
            buildChanged = true;
        }
        var defineSymbols = EditorGUILayout.TextField(
            "Scripting Defines", string.Join(';', build.ScriptingDefineSymbols));
        var parsedDefines = defineSymbols.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!parsedDefines.SequenceEqual(build.ScriptingDefineSymbols, StringComparer.Ordinal))
        {
            build.ScriptingDefineSymbols = parsedDefines.ToList();
            buildChanged = true;
        }
        var updatePolicy = (PlayerContentUpdatePolicy)EditorGUILayout.EnumPopup(
            "Startup Update", build.ContentUpdatePolicy);
        if (updatePolicy != build.ContentUpdatePolicy)
        {
            build.ContentUpdatePolicy = updatePolicy;
            buildChanged = true;
        }
        var compressBundles = EditorGUILayout.Toggle("Compress Resources",
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
                var codesignKey = EditorGUILayout.TextField(
                    "Signing Certificate", build.IosCodesignKey);
                if (!codesignKey.Equals(build.IosCodesignKey, StringComparison.Ordinal))
                {
                    build.IosCodesignKey = codesignKey;
                    buildChanged = true;
                }
                var provisioningProfile = EditorGUILayout.TextField(
                    "Provisioning Profile", build.IosProvisioningProfile);
                if (!provisioningProfile.Equals(
                        build.IosProvisioningProfile, StringComparison.Ordinal))
                {
                    build.IosProvisioningProfile = provisioningProfile;
                    buildChanged = true;
                }
            }
        }
        var runInBackground = EditorGUILayout.Toggle("Run In Background", value.RunInBackground);
        if (runInBackground != value.RunInBackground)
        {
            value.RunInBackground = runInBackground;
            changed = true;
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
                TargetId = BEngine.Platform.BuildTargetUtility.Current.InferCurrentDesktopPlatformId()
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
                TargetId = BEngine.Platform.BuildTargetUtility.Current.InferCurrentDesktopPlatformId()
            };
        }
    }

    private static void SavePlayerBuildSettings(PlayerBuildSettings settings)
    {
        if (string.IsNullOrWhiteSpace(_playerBuildProjectPath)) return;
        try
        {
            PlayerBuildSettingsStore.Save(_playerBuildProjectPath, settings);
            _ = PlayerBuildProfileStore.ApplyActiveScriptingDefines(_playerBuildProjectPath);
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

    private static int DrawPlatformTabs(
        IReadOnlyList<BuildPlatformDescriptor> platforms,
        int selectedIndex)
    {
        var next = selectedIndex;
        for (var row = 0; row < 2; row++)
        {
            GUILayout.BeginHorizontal();
            try
            {
                var start = row * 3;
                var end = Math.Min(start + 3, platforms.Count);
                for (var index = start; index < end; index++)
                {
                    var selected = index == selectedIndex;
                    var style = selected
                        ? EditorStyles.treeViewRowSelected
                        : EditorStyles.miniButton;
                    if (GUILayout.Button(new GUIContent(
                                PlatformLabel(platforms[index].Platform),
                                tooltip: "CPU architecture is selected automatically"),
                            style, GUILayout.ExpandWidth(true)))
                        next = index;
                }
            }
            finally
            {
                GUILayout.EndHorizontal();
            }
        }
        return next;
    }

}
