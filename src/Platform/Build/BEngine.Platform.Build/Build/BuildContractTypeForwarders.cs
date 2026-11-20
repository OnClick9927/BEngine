using System.Runtime.CompilerServices;

// Build options and policy contracts live in BEngine.Editor so the core build
// pipeline and content packages do not reference the platform implementation
// assembly. Keep these forwarders for older editor extensions that still load
// BEngine.Platform.Build directly.
[assembly: TypeForwardedTo(typeof(BEngine.Editor.AndroidBuildOptions))]
[assembly: TypeForwardedTo(typeof(BEngine.Editor.IosBuildOptions))]
[assembly: TypeForwardedTo(typeof(BEngine.Editor.PlatformContentBuildPolicy))]
[assembly: TypeForwardedTo(typeof(BEngine.Editor.PlatformGraphicsBuildPolicy))]
[assembly: TypeForwardedTo(typeof(BEngine.Editor.PlatformTextBuildPolicy))]
[assembly: TypeForwardedTo(typeof(BEngine.Editor.PlayerGarbageCollectionBuildSettings))]
[assembly: TypeForwardedTo(typeof(BEngine.Editor.PlayerGarbageCollectionMode))]
[assembly: TypeForwardedTo(typeof(BEngine.Editor.WebBuildOptions))]
[assembly: TypeForwardedTo(typeof(BEngine.Editor.WebFontBackend))]
[assembly: TypeForwardedTo(typeof(BEngine.Editor.WebScriptCompilationMode))]
[assembly: TypeForwardedTo(typeof(BEngine.Editor.WindowsBuildOptions))]
