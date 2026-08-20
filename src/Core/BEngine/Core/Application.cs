using System.Diagnostics;
using System.Runtime.InteropServices;

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
    private static string _version = "0.1.0";
    private static string _companyName = "DefaultCompany";
    private static bool _isEditor;
    private static bool _isPlaying = true;
    private static int _targetFrameRate = -1;
    private static string _dataPath = AppContext.BaseDirectory;

    public static event Func<bool>? wantsToQuit
    {
        add { MainThreadGuard.Ensure(); _wantsToQuit += value; }
        remove { MainThreadGuard.Ensure(); _wantsToQuit -= value; }
    }
    public static event Action? quitting
    {
        add { MainThreadGuard.Ensure(); _quitting += value; }
        remove { MainThreadGuard.Ensure(); _quitting -= value; }
    }
    public static event Action<bool>? focusChanged
    {
        add { MainThreadGuard.Ensure(); _focusChanged += value; }
        remove { MainThreadGuard.Ensure(); _focusChanged -= value; }
    }
    public static event Action<bool>? pauseStateChanged
    {
        add { MainThreadGuard.Ensure(); _pauseStateChanged += value; }
        remove { MainThreadGuard.Ensure(); _pauseStateChanged -= value; }
    }
    public static event Action? lowMemory
    {
        add { MainThreadGuard.Ensure(); _lowMemory += value; }
        remove { MainThreadGuard.Ensure(); _lowMemory -= value; }
    }
    public static string productName
    {
        get { MainThreadGuard.Ensure(); return _productName; }
        internal set => _productName = value;
    }
    public static string version
    {
        get { MainThreadGuard.Ensure(); return _version; }
        internal set => _version = value;
    }
    public static string companyName
    {
        get { MainThreadGuard.Ensure(); return _companyName; }
        internal set => _companyName = value;
    }
    public static bool isEditor
    {
        get { MainThreadGuard.Ensure(); return _isEditor; }
        internal set => _isEditor = value;
    }
    public static bool isPlaying
    {
        get { MainThreadGuard.Ensure(); return _isPlaying; }
        internal set => _isPlaying = value;
    }
    public static bool isFocused
    {
        get { MainThreadGuard.Ensure(); return _isFocused; }
    }
    public static int targetFrameRate
    {
        get { MainThreadGuard.Ensure(); return _targetFrameRate; }
        set { MainThreadGuard.Ensure(); _targetFrameRate = value; }
    }
    public static string dataPath
    {
        get { MainThreadGuard.Ensure(); return _dataPath; }
        internal set => _dataPath = value;
    }
    public static string streamingAssetsPath
    {
        get { MainThreadGuard.Ensure(); return Path.Combine(_dataPath, "StreamingAssets"); }
    }
    public static string persistentDataPath
    {
        get
        {
            MainThreadGuard.Ensure();
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                _companyName, _productName);
        }
    }
    public static string temporaryCachePath
    {
        get { MainThreadGuard.Ensure(); return Path.Combine(Path.GetTempPath(), _companyName, _productName); }
    }
    public static RuntimePlatform platform
    {
        get { MainThreadGuard.Ensure(); return ResolvePlatform(_isEditor); }
    }

    public static void OpenURL(string url)
    {
        MainThreadGuard.Ensure();
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public static void Quit()
    {
        MainThreadGuard.Ensure();
        if (_wantsToQuit is not null)
        {
            foreach (Func<bool> callback in _wantsToQuit.GetInvocationList())
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
        MainThreadGuard.Ensure();
        if (_isFocused == focused) return;
        _isFocused = focused;
        RuntimeLifecycle.Invoke(_focusChanged, focused, nameof(focusChanged));
    }

    internal static void SetPaused(bool paused)
    {
        MainThreadGuard.Ensure();
        RuntimeLifecycle.Invoke(_pauseStateChanged, paused, nameof(pauseStateChanged));
    }

    internal static void RaiseLowMemory()
    {
        MainThreadGuard.Ensure();
        RuntimeLifecycle.Invoke(_lowMemory, nameof(lowMemory));
    }

    private static RuntimePlatform ResolvePlatform(bool editor)
    {
        if (OperatingSystem.IsWindows()) return editor ? RuntimePlatform.WindowsEditor : RuntimePlatform.WindowsPlayer;
        if (OperatingSystem.IsMacOS()) return editor ? RuntimePlatform.OSXEditor : RuntimePlatform.OSXPlayer;
        return editor ? RuntimePlatform.LinuxEditor : RuntimePlatform.LinuxPlayer;
    }
}
