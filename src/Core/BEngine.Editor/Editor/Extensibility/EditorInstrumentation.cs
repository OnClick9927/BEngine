using BEngine.Editor.Rendering;
using BEngine.Rendering.Rhi;
namespace BEngine.Editor;
public static class EditorInstrumentation
{
    public static event Func<Type, string, IDisposable>? SampleRequested;
    public static event Func<bool>? FrameMeasurementRequested;
    public static event Func<IGraphicsDevice, IGraphicsDevice>? DeviceCreated;
    public delegate bool TextureResolver(string source, out GpuCanvasTextureData texture);
    public static event TextureResolver? TextureRequested;
    public static IDisposable? BeginSample(Type type, string method) => SampleRequested?.Invoke(type, method);
    internal static bool MeasureFrames
    {
        get
        {
            foreach (var handler in Delegate.EnumerateInvocationList(FrameMeasurementRequested))
                if (handler()) return true;
            return false;
        }
    }
    internal static IGraphicsDevice Decorate(IGraphicsDevice device)
    {
        foreach (var handler in Delegate.EnumerateInvocationList(DeviceCreated)) device = handler(device);
        return device;
    }
    internal static bool TryResolveTexture(string source, out GpuCanvasTextureData texture)
    {
        foreach (var handler in Delegate.EnumerateInvocationList(TextureRequested))
            if (handler(source, out texture)) return true;
        texture = default; return false;
    }
}
