using BEngine.Build;
using BEngine.Platform.DotNet;
using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Platform;

public static class AndroidPlatform
{
    public static BuildTargetPlatform Target => BuildTargetPlatform.Android;

    public static IServiceCollection AddBEngineAndroid(this IServiceCollection services) =>
        services.AddBEngineDotNetPlatform();

    public static void Initialize()
    {
        RuntimeServiceContributions.RegisterDefault<IPlatformAudioOutputFactory>(AndroidNativeAudioOutputFactory.Shared);
        DotNetPlatformServices.Initialize();

        // Android uses a separate runtime profile.  Keep the default cadence
        // at the display-friendly 60 Hz and avoid desktop quality defaults
        // that cause unnecessary heat on mobile GPUs.  Games may still opt in
        // to a different target after startup through Application APIs.
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 1;
        QualitySettings.antiAliasing = 0;
        QualitySettings.shadowDistance = 0;
        QualitySettings.realtimeReflectionProbes = false;
    }

    public static void SuspendAudioOutputs() => AndroidNativeAudioOutput.SetSuspended(true);
    public static void ResumeAudioOutputs() => AndroidNativeAudioOutput.SetSuspended(false);
}
