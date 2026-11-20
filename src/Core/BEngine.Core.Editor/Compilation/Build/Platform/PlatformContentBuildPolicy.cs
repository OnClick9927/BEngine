using BEngine.Build;

namespace BEngine.Editor;

/// <summary>Content-codec capabilities of the built-in platform hosts.</summary>
public static class PlatformContentBuildPolicy
{
    /// <summary>Honors the archive writer's request only when the built-in runtime can decode Brotli.
    /// The .NET browser Brotli assembly is a PlatformNotSupported implementation; HTTP transfer
    /// compression is a separate transport concern and is unaffected by this setting.</summary>
    public static bool CompressBuiltInArchive(BuildTargetPlatform? platform, bool requested) =>
        requested && platform != BuildTargetPlatform.Web;
}
