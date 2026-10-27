using BEngine.Build;

namespace BEngine.Editor;

internal static class PlayerResourceArchiveCompression
{
    // .NET's browser Brotli assembly is a PlatformNotSupported implementation.
    // Built-in archives must remain readable there, even when AB ZIP compression
    // is requested. HTTP transfer compression can still compress Web downloads.
    internal static bool IsEnabled(BuildTargetPlatform? platform, bool requested) =>
        requested && platform != BuildTargetPlatform.Web;
}
