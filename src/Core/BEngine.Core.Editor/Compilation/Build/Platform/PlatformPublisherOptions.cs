namespace BEngine.Editor;

public sealed class WebBuildOptions
{
    public WebFontBackend WebFontBackend { get; init; }
    public WebScriptCompilationMode WebScriptCompilation { get; init; }
}

public sealed class WindowsBuildOptions
{
    public PlayerGarbageCollectionMode WindowsGarbageCollection { get; init; }
    public int WindowsGcHeapCount { get; init; }
}

public sealed class AndroidBuildOptions
{
    public string AndroidApplicationIdentifier { get; init; } = "com.example.game";
    public int AndroidMinimumApiLevel { get; init; } = 26;
    public bool AndroidBuildAppBundle { get; init; } = true;
}

public sealed class IosBuildOptions
{
    public string IosBundleIdentifier { get; init; } = "com.example.game";
    public string IosMinimumVersion { get; init; } = "15.0";
    public string IosCodesignKey { get; init; } = string.Empty;
    public string IosProvisioningProfile { get; init; } = string.Empty;
}

