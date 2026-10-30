using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System;
using BEngine.HotUpdate;

namespace BEngine.Player.Hosts;

internal static class PlayerTextHost
{
    // Platform SkiaSharp/HarfBuzzSharp assemblies carry the correct native import names,
    // including static iOS/Wasm bindings. Downloaded portable copies must share these.
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Keep native text bindings in the static Player host.")]
    internal static void Initialize()
    {
#if !BENGINE_WEB_FONTS
        ManagedCodeHostAssemblies.Register(typeof(SkiaSharp.SKFont).Assembly);
        ManagedCodeHostAssemblies.Register(typeof(SkiaSharp.HarfBuzz.SKShaper).Assembly);
#endif
        ManagedCodeHostAssemblies.Register(typeof(HarfBuzzSharp.Blob).Assembly);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
#if !BENGINE_WEB_FONTS
            NativeLibrary.SetDllImportResolver(typeof(SkiaSharp.SKFont).Assembly, SystemPlayerNativeLibraryLoader.ResolveDesktopImport);
#endif
            NativeLibrary.SetDllImportResolver(typeof(HarfBuzzSharp.Blob).Assembly, SystemPlayerNativeLibraryLoader.ResolveDesktopImport);
        }
    }
}
