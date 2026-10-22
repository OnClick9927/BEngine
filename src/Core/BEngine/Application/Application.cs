using System.Globalization;

namespace BEngine;

public static class Application
{
    private static bool _isFocused = true;
    private static Func<bool>? _wantsToQuit;
    private static Action? _quitting;
    private static Action<bool>? _focusChanged;
    private static Action<bool>? _pauseStateChanged;
    private static Action? _lowMemory;
    private static string _productName = "BEngine Game";
    private static string _version = "1.0.0";
    private static string _companyName = "DefaultCompany";
    private static bool _isEditor;
    private static bool _isPlaying = true;
    private static string _dataPath = string.Empty;
    private static bool _runInBackground;

    public static event Func<bool>? wantsToQuit
    {
        add { _wantsToQuit += value; }
        remove { _wantsToQuit -= value; }
    }
    public static event Action? quitting
    {
        add { _quitting += value; }
        remove { _quitting -= value; }
    }
    public static event Action<bool>? focusChanged
    {
        add { _focusChanged += value; }
        remove { _focusChanged -= value; }
    }
    public static event Action<bool>? pauseStateChanged
    {
        add { _pauseStateChanged += value; }
        remove { _pauseStateChanged -= value; }
    }
    public static event Action? lowMemory
    {
        add { _lowMemory += value; }
        remove { _lowMemory -= value; }
    }
    public static string productName
    {
        get { return _productName; }
        internal set => _productName = value;
    }
    public static string version
    {
        get { return _version; }
        internal set => _version = value;
    }
    public static string engineVersion => "1.0.0";
    public static string identifier => $"{SanitizeIdentifier(_companyName)}.{SanitizeIdentifier(_productName)}";
    public static string buildGUID => typeof(Application).Assembly.ManifestModule.ModuleVersionId.ToString("N");
    public static bool isBatchMode => RuntimeServiceAccess.PlatformService.GetCommandLineArgs().Any(argument =>
        argument.Equals("-batchmode", StringComparison.OrdinalIgnoreCase) ||
        argument.Equals("--batchmode", StringComparison.OrdinalIgnoreCase));
    public static bool isMobilePlatform => platform is RuntimePlatform.AndroidPlayer or RuntimePlatform.IPhonePlayer;
    public static bool isConsolePlatform => false;
    public static bool runInBackground
    {
        get => _runInBackground;
        set => _runInBackground = value;
    }
    public static SystemLanguage systemLanguage =>
        ResolveSystemLanguage(RuntimeServiceAccess.PlatformService.CurrentUICulture);
    public static NetworkReachability internetReachability => RuntimeServiceAccess.PlatformService.IsNetworkAvailable
        ? NetworkReachability.ReachableViaLocalAreaNetwork
        : NetworkReachability.NotReachable;
    public static string companyName
    {
        get { return _companyName; }
        internal set => _companyName = value;
    }
    public static bool isEditor
    {
        get { return _isEditor; }
        internal set => _isEditor = value;
    }
    public static bool isPlaying
    {
        get { return _isPlaying; }
        internal set => _isPlaying = value;
    }
    public static bool isFocused
    {
        get { return _isFocused; }
    }
    public static int targetFrameRate
    {
        get { return BEngine.Platform.RenderSettingsUtility.Current.targetFrameRate; }
        set { BEngine.Platform.RenderSettingsUtility.Current.targetFrameRate = value; }
    }
    public static string dataPath
    {
        get
        {
            return string.IsNullOrWhiteSpace(_dataPath)
                ? RuntimeServiceAccess.ApplicationPaths.BaseDirectory
                : _dataPath;
        }
        internal set => _dataPath = value;
    }
    public static string streamingAssetsPath
    {
        get { return RuntimeServiceAccess.PathService.Combine(dataPath, "StreamingAssets"); }
    }
    public static string persistentDataPath
    {
        get
        {
            return RuntimeServiceAccess.ApplicationPaths.GetPersistentDataDirectory(
                _companyName, _productName);
        }
    }
    public static string temporaryCachePath
    {
        get
        {
            return RuntimeServiceAccess.PathService.Combine(
                RuntimeServiceAccess.ApplicationPaths.TemporaryDirectory,
                _companyName,
                _productName);
        }
    }
    public static RuntimePlatform platform
    {
        get { return ResolvePlatform(_isEditor); }
    }

    public static void OpenURL(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        RuntimeServiceAccess.PlatformService.OpenExternalUri(url);
    }

    public static void Quit()
    {
        if (_wantsToQuit is not null)
        {
            foreach (Func<bool> callback in Delegate.EnumerateInvocationList(_wantsToQuit))
            {
                try
                {
                    if (!callback()) return;
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Application.wantsToQuit callback failed: {exception.Message}");
                }
            }
        }

        RuntimeLifecycle.Invoke(_quitting, nameof(quitting));
        _isPlaying = false;
    }

    internal static void SetFocus(bool focused)
    {
        if (_isFocused == focused) return;
        _isFocused = focused;
        if (!focused) Input.ResetInputAxes();
        RuntimeLifecycle.Invoke(_focusChanged, focused, nameof(focusChanged));
    }

    internal static void SetPaused(bool paused)
    {
        RuntimeLifecycle.Invoke(_pauseStateChanged, paused, nameof(pauseStateChanged));
    }

    internal static void RaiseLowMemory()
    {
        RuntimeLifecycle.Invoke(_lowMemory, nameof(lowMemory));
    }

    private static RuntimePlatform ResolvePlatform(bool editor)
    {
        var platform = RuntimeServiceAccess.PlatformService;
        if (platform.IsBrowser) return RuntimePlatform.WebGLPlayer;
        // The exported core assembly is shared by desktop and mobile hosts.
        // Resolve the running OS rather than the SDK's compilation symbols.
        if (!editor && OperatingSystem.IsAndroid()) return RuntimePlatform.AndroidPlayer;
        if (!editor && OperatingSystem.IsIOS()) return RuntimePlatform.IPhonePlayer;
        if (platform.IsWindows) return editor ? RuntimePlatform.WindowsEditor : RuntimePlatform.WindowsPlayer;
        if (platform.IsMacOS) return editor ? RuntimePlatform.OSXEditor : RuntimePlatform.OSXPlayer;
        return editor ? RuntimePlatform.LinuxEditor : RuntimePlatform.LinuxPlayer;
    }

    private static SystemLanguage ResolveSystemLanguage(CultureInfo culture) => culture.TwoLetterISOLanguageName switch
    {
        "zh" when culture.Name.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
                  culture.Name.EndsWith("-TW", StringComparison.OrdinalIgnoreCase) ||
                  culture.Name.EndsWith("-HK", StringComparison.OrdinalIgnoreCase) =>
            SystemLanguage.ChineseTraditional,
        "zh" => SystemLanguage.ChineseSimplified,
        "en" => SystemLanguage.English,
        "fr" => SystemLanguage.French,
        "de" => SystemLanguage.German,
        "it" => SystemLanguage.Italian,
        "ja" => SystemLanguage.Japanese,
        "ko" => SystemLanguage.Korean,
        "pt" => SystemLanguage.Portuguese,
        "ru" => SystemLanguage.Russian,
        "es" => SystemLanguage.Spanish,
        _ => SystemLanguage.Unknown
    };

    private static string SanitizeIdentifier(string value)
    {
        var normalized = new string((value ?? string.Empty).Where(static character =>
            char.IsLetterOrDigit(character) || character == '_').ToArray());
        return normalized.Length == 0 ? "BEngine" : normalized;
    }
}
