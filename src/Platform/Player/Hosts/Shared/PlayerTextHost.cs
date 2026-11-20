using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System;
using BEngine.Player.HotUpdate;
using BEngine.Hosting;

namespace BEngine.Player.Hosts;

internal static class PlayerTextHost
{
    // Platform SkiaSharp/HarfBuzzSharp assemblies carry the correct native import names,
    // including static iOS/Wasm bindings. Downloaded portable copies must share these.
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Keep native text bindings in the static Player host.")]
    internal static void Initialize()
    {
#if BENGINE_WEB_FONTS
        BEngine.Platform.WebFontRasterizer.Register();
#endif
#if !BENGINE_WEB_FONTS
        BEngine.Hosting.HostAssemblyRegistry.Register(typeof(SkiaSharp.SKFont).Assembly);
        BEngine.Hosting.HostAssemblyRegistry.Register(typeof(SkiaSharp.HarfBuzz.SKShaper).Assembly);
#endif
        BEngine.Hosting.HostAssemblyRegistry.Register(typeof(HarfBuzzSharp.Blob).Assembly);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
#if !BENGINE_WEB_FONTS
            NativeLibrary.SetDllImportResolver(typeof(SkiaSharp.SKFont).Assembly,
                static (name, assembly, path) => NativeLibraryUtility.Current.ResolveImport(name, assembly, path));
#endif
            NativeLibrary.SetDllImportResolver(typeof(HarfBuzzSharp.Blob).Assembly,
                static (name, assembly, path) => NativeLibraryUtility.Current.ResolveImport(name, assembly, path));
        }
    }
}
