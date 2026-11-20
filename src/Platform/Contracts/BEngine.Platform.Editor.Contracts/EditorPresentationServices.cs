using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace BEngine.Editor;
public static class EditorPresentationServices
{
    [ModuleInitializer]
    public static void Initialize()
    {
        BEngine.Hosting.HostAssemblyRegistry.Register(typeof(EditorPresentationServices).Assembly);
        RuntimeServiceContributions.Register(Configure);
    }
    public static void Configure(IServiceCollection services)
    {
        // Presentation is optional in compiler/import tools. Resolve its backend only
        // when the capability is requested, without forcing window services into a
        // validated headless service graph. Missing capabilities still fail on use.
        services.TryAddSingleton(provider => new NativeWindowUtility(provider.GetRequiredService<IEditorNativeWindowFactory>()));
        services.TryAddSingleton(provider => new DesktopUtility(provider.GetRequiredService<IEditorDesktopService>()));
        services.TryAddSingleton(provider => new ClipboardUtility(provider.GetRequiredService<IClipboardService>()));
        services.TryAddSingleton(provider => new EditorTextUtility(provider.GetRequiredService<IEditorTextRasterizer>()));
        services.TryAddSingleton(provider => new FileDialogUtility(provider.GetRequiredService<IFileDialogService>()));
    }
}
