using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Editor.Diagnostics;
using BEngine.Editor.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

internal static class DiagnosticsHostRegistration
{
    private static readonly Dictionary<IEditorView, FrameDebuggerGamePreviewView> Previews = [];
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Register optional diagnostics hooks.")]
    internal static void Register()
    {
        EditorProfiler.Sink = new EditorProfilerSink();
        EditorInstrumentation.SampleRequested += BeginSample;
        EditorInstrumentation.FrameMeasurementRequested += MeasureFrames;
        EditorInstrumentation.DeviceCreated += Decorate;
        EditorInstrumentation.TextureRequested += FrameDebuggerPreviewRegistry.TryResolve;
        var debugger = FrameDebuggerService.Shared;
        debugger.Changed += EditorViewportExtensions.NotifyChanged;
        EditorViewportExtensions.BeginRenderHandler = BeginRender;
        EditorViewportExtensions.IsDebugTargetHandler = IsTarget;
        EditorViewportExtensions.RequiresRenderHandler = debugger.RequiresRender;
        EditorViewportExtensions.ReleaseTargetHandler = Release;
        EditorViewportExtensions.DrawOverlayHandler = DrawOverlay;
        RuntimeAssemblyCleanup.Register(Cleanup);
    }
    private static IDisposable BeginSample(Type type, string method) => EditorProfiler.BeginMethodSample(type, method, EditorProfilerDomain.Editor);
    private static bool MeasureFrames() => EditorProfiler.Recording;
    private static IGraphicsDevice Decorate(IGraphicsDevice device) => new FrameDebugGraphicsDevice(device, ownsDevice: true);
    private static bool IsTarget(object target) => FrameDebuggerService.Shared.Enabled && FrameDebuggerService.Shared.IsTarget(target);
    private static IDisposable? BeginRender(object target, IGraphicsDevice device, string name, GraphicsRect area, int width, int height) =>
        FrameDebuggerService.Shared.Enabled && device is FrameDebugGraphicsDevice debug
            ? FrameDebuggerService.Shared.BeginRender(target, debug, name, new FrameDebugPreviewArea(area, width, height)) : null;
    private static void Release(object target)
    {
        FrameDebuggerService.Shared.ReleaseTarget(target);
        if (target is IEditorView view && Previews.Remove(view, out var preview)) preview.Dispose();
    }
    private static void DrawOverlay(IEditorView view, Rect area)
    {
        var debugger = FrameDebuggerService.Shared;
        if (!debugger.Enabled || !debugger.IsTarget(view) || debugger.StepLimit < 0)
        {
            if (Previews.TryGetValue(view, out var existing)) existing.Clear();
            return;
        }
        if (!Previews.TryGetValue(view, out var preview)) Previews.Add(view, preview = new());
        preview.Draw(area, debugger.Preview, debugger.StepLimit);
    }
    private static void Cleanup(Assembly assembly)
    {
        if (assembly != typeof(DiagnosticsHostRegistration).Assembly) return;
        FrameDebuggerService.Shared.Disable();
        FrameDebuggerService.Shared.DetachEditorEvents();
        FrameDebuggerService.Shared.Changed -= EditorViewportExtensions.NotifyChanged;
        EditorProfiler.Recording = false;
        EditorProfiler.Clear();
        EditorProfiler.Sink = null;
        EditorInstrumentation.SampleRequested -= BeginSample;
        EditorInstrumentation.FrameMeasurementRequested -= MeasureFrames;
        EditorInstrumentation.DeviceCreated -= Decorate;
        EditorInstrumentation.TextureRequested -= FrameDebuggerPreviewRegistry.TryResolve;
        EditorViewportExtensions.BeginRenderHandler = null;
        EditorViewportExtensions.IsDebugTargetHandler = null;
        EditorViewportExtensions.RequiresRenderHandler = null;
        EditorViewportExtensions.ReleaseTargetHandler = null;
        EditorViewportExtensions.DrawOverlayHandler = null;
        foreach (var preview in Previews.Values) preview.Dispose();
        Previews.Clear();
    }
}
