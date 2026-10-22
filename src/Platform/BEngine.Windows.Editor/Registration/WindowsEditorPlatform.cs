using BEngine.ProjectSystem.Editor;
using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Editor.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace BEngine.Platform;
public static class WindowsEditorPlatform
{
    [ModuleInitializer]
    public static void Initialize()
    {
        DotNetEditorPlatform.Initialize();
        RuntimeServiceContributions.Register(Configure);
    }
    public static void Configure(IServiceCollection services)
    {
        services.TryAddSingleton<IEditorUtilityPlatform, WindowsEditorUtilityPlatform>();
        services.TryAddSingleton<IEditorNativeWindowFactory, ImGuiNativeWindowFactory>();
        services.TryAddSingleton<IGpuCanvasResourceResolver>(FileGpuCanvasResourceResolver.Shared);
        services.TryAddSingleton<IEditorDesktopService, WindowsEditorDesktopService>();
        services.TryAddSingleton<IShaderCompiler, ShadercCompiler>();
        services.TryAddSingleton<IFileDialogService, WindowsFileDialogService>();
        services.TryAddSingleton<IClipboardService, SystemClipboard>();
        services.TryAddSingleton<IEditorTextRasterizer, WindowsEditorTextRasterizer>();
    }
}
