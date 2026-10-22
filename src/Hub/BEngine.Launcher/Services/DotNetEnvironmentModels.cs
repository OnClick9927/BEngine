namespace BEngine.Launcher;

internal sealed record DotNetEnvironmentSnapshot(
    string? DesktopRuntimeVersion = null,
    string? SdkVersion = null,
    string? RuntimeRoot = null,
    string? SdkRoot = null)
{
    public bool HasDesktopRuntime => DesktopRuntimeVersion is not null && RuntimeRoot is not null;
    public bool HasSdk => SdkVersion is not null && SdkRoot is not null;
    public bool IsReady => HasDesktopRuntime && HasSdk;
    public string? DotNetRoot => RuntimeRoot;
    public string? DotNetPath => (SdkRoot ?? RuntimeRoot) is { } root ? Path.Combine(root, "dotnet.exe") : null;
    public string MissingComponents => !HasSdk && !HasDesktopRuntime ? ".NET 10 SDK and Desktop Runtime (x64)" :
        !HasSdk ? ".NET 10 SDK (x64)" : !HasDesktopRuntime ? ".NET 10 Desktop Runtime (x64)" : string.Empty;
}

internal sealed record DotNetEnvironmentProgress(
    string Stage,
    string Message,
    long Completed = 0,
    long Total = 0,
    double? Percent = null)
{
    public long BytesReceived => Completed;
    public long TotalBytes => Total;
}

internal sealed record DotNetEnvironmentInstallResult(
    DotNetEnvironmentSnapshot Snapshot,
    bool Installed = false,
    bool RestartRequired = false,
    bool Cancelled = false)
{
    public bool IsReady => Snapshot.IsReady;
}

internal sealed record DotNetEnvironmentInstaller(string Version, Uri Url, string Sha512, bool IsSdk);
