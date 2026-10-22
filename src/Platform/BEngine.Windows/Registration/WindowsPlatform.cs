using BEngine.Build;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BEngine.Rendering;

namespace BEngine.Platform;

public static class WindowsPlatform
{
    public static BuildTargetPlatform Target => BuildTargetPlatform.Windows;

    public static IServiceCollection AddBEngineWindows(this IServiceCollection services)
    {
        services.TryAddSingleton<ITextRasterizer, WindowsTextRasterizer>();
        services.TryAddSingleton<ITextInputService, WindowsTextInputService>();
        services.TryAddSingleton<ITouchInputService, WindowsTouchInputService>();
        return services.AddBEngineDesktopPlatform();
    }

    private static readonly ServiceProvider Services = new ServiceCollection().AddBEngineWindows().BuildServiceProvider();
    public static void Initialize()
    {
        PlatformRuntime.Initialize(Services);
        PlatformAccessibility.adapter ??= new WindowsAccessibilityAdapter();
    }
}
