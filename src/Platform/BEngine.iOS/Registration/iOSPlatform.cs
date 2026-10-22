using BEngine.Build;
using BEngine.DependencyInjection;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Platform;

public static class iOSPlatform
{
    public static BuildTargetPlatform Target => BuildTargetPlatform.IOS;

    public static IServiceCollection AddBEngineiOS(this IServiceCollection services) =>
        services.AddBEngineDotNetPlatform();

    public static void Initialize()
    {
        RuntimeServiceContributions.RegisterDefault<IPlatformAudioOutputFactory>(
            IosAudioPlatformOutputFactory.Shared);
        DotNetPlatformServices.Initialize();
    }

    public static void SuspendAudioOutputs() => IosAudioOutputLifecycle.Suspend();

    public static void ResumeAudioOutputs() => IosAudioOutputLifecycle.Resume();
}
