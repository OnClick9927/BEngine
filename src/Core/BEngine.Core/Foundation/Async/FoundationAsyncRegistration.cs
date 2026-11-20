using System.Runtime.CompilerServices;

namespace BEngine;

internal static class FoundationAsyncRegistration
{
    [ModuleInitializer]
    internal static void Initialize() => AsyncHostBridge.RegisterHost(
        static () => Application.targetFrameRate,
        static message => RuntimeServices.PlatformService.WriteDiagnostic(message),
        static exception => Debug.LogException(exception),
        static callback => Application.quitting += callback,
        static callback => Application.quitting -= callback);
}
