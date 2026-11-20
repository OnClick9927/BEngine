using BEngine.DependencyInjection;
using BEngine.Hosting;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Platform;

public static class DesktopPlatformServices
{
    private static readonly ServiceProvider Defaults = new ServiceCollection()
        .AddBEngineDesktopPlatform().BuildServiceProvider();

    public static void Initialize() => PlatformRuntime.Initialize(Defaults);

    public static IServiceCollection AddBEngineDesktopPlatform(this IServiceCollection services)
    {
        services.AddBEngineDotNetPlatform();
        ConfigureHostServices(services);
        RuntimeServiceContributions.Register(ConfigureHostServices);
        return services;
    }

    private static void ConfigureHostServices(IServiceCollection services)
    {
        services.TryAddSingleton<IPlatformAudioOutputFactory>(DesktopAudioOutputFactory.Shared);
        services.TryAddSingleton<IHostWindowFactory, SilkHostWindowFactory>();
        services.TryAddSingleton<IHostRenderContextFactory, SilkHostRenderContextFactory>();
        services.TryAddSingleton<IHostInputContextFactory, SilkHostInputContextFactory>();
        // Refine only the generic default; an explicitly supplied host loader wins.
        var loader = Scan1231(services);
        [global::BEngine.SkipLoopGuard]
        global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor? Scan1231(global::System.Collections.Generic.IEnumerable<global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor> scanSource1231)
        {
            ArgumentNullException.ThrowIfNull(scanSource1231, "source");
            if (scanSource1231 is global::System.Collections.Generic.IList<global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor> scanList1231)
            {
                for (var scanIndex1231 = scanList1231.Count - 1; scanIndex1231 >= 0; scanIndex1231--)
                {
                    var scanItem1231 = scanList1231[scanIndex1231];
                    if (Match1231(scanItem1231))
                        return scanItem1231;
                }
        
                return default!;
            }
        
            global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor? result = default!;
            foreach (var scanItem1231 in scanSource1231)
                if (Match1231(scanItem1231))
                    result = scanItem1231;
            return result;
            bool Match1231(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor descriptor) => descriptor.ServiceType == typeof(INativeLibraryLoader);
        }
        if (loader?.ImplementationType == typeof(SystemNativeLibraryLoader)) services.Remove(loader);
        services.TryAddSingleton<INativeLibraryLoader, DesktopNativeLibraryLoader>();
    }
}
