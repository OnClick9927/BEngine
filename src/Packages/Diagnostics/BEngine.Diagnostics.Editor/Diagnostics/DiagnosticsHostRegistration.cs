using System.Runtime.CompilerServices;
using BEngine.Editor.Diagnostics;
namespace BEngine.Editor;
internal static class DiagnosticsHostRegistration
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Register optional diagnostics hooks.")]
    internal static void Register()
    {
        EditorInstrumentation.SampleRequested += (type, method) => EditorProfiler.BeginMethodSample(type, method, EditorProfilerDomain.Editor);
        EditorInstrumentation.FrameMeasurementRequested += () => EditorProfiler.Recording;
        EditorInstrumentation.DeviceCreated += device => new FrameDebugGraphicsDevice(device, ownsDevice: true);
        EditorInstrumentation.TextureRequested += FrameDebuggerPreviewRegistry.TryResolve;
    }
}
