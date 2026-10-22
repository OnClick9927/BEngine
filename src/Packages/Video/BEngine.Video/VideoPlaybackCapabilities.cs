namespace BEngine.Video;

public enum VideoPlatformFamily { Windows, Linux, MacOS, Web, Android, IOS, Unknown }
public enum VideoClipEncoding { PortableFrames, OriginalContainer }

public readonly record struct VideoPlaybackCapabilities(
    VideoPlatformFamily Platform,
    bool PortableFrames,
    bool OriginalContainers,
    bool HttpSources,
    bool HardwareDecoding)
{
    public static VideoPlaybackCapabilities Current => BEngine.Platform.PlatformVideoDecoding.OpenVideo is not null || BEngine.Platform.PlatformVideoDecoding.OpenVideoAsync is not null
        ? new(DetectPlatform(), true, true, true, false)
        : (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && FfmpegVideoFrameReader.IsAvailable
            ? new(DetectPlatform(), true, true, true, false) : ForPlatform(DetectPlatform());

    public static VideoPlaybackCapabilities ForPlatform(VideoPlatformFamily platform) => platform switch
    {
        VideoPlatformFamily.Windows => new(platform, true, true, true, true),
        VideoPlatformFamily.Linux or VideoPlatformFamily.MacOS or VideoPlatformFamily.Web or
            VideoPlatformFamily.Android or VideoPlatformFamily.IOS => new(platform, true, false, false, false),
        _ => new(platform, true, false, false, false)
    };

    public bool Supports(VideoClipEncoding encoding, VideoDecodeMode mode = VideoDecodeMode.Automatic) =>
        Enum.IsDefined(encoding) && Enum.IsDefined(mode) && encoding switch
        {
            VideoClipEncoding.PortableFrames => mode != VideoDecodeMode.HardwareRequired && PortableFrames,
            VideoClipEncoding.OriginalContainer => OriginalContainers &&
                (mode != VideoDecodeMode.HardwareRequired || HardwareDecoding),
            _ => false
        };

    private static VideoPlatformFamily DetectPlatform()
    {
        if (OperatingSystem.IsBrowser()) return VideoPlatformFamily.Web;
        if (OperatingSystem.IsAndroid()) return VideoPlatformFamily.Android;
        if (OperatingSystem.IsIOS()) return VideoPlatformFamily.IOS;
        if (OperatingSystem.IsWindows()) return VideoPlatformFamily.Windows;
        if (OperatingSystem.IsLinux()) return VideoPlatformFamily.Linux;
        if (OperatingSystem.IsMacOS()) return VideoPlatformFamily.MacOS;
        return VideoPlatformFamily.Unknown;
    }
}
